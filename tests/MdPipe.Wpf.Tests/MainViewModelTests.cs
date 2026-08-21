using System.IO;
using FluentAssertions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using MdPipe.Wpf;
using MdPipe.Wpf.Services;
using MdPipe.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Wpf.Tests;

/// <summary>
/// Covers the batch logic the desktop app runs on. Everything that talks to Windows goes through
/// <see cref="IDialogService"/>, so nothing here can hang on a message box waiting to be dismissed.
/// </summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mdpipe-wpf-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeConverter _converter = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeEnvironment _environment = new();

    public MainViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private MainViewModel BuildSut()
    {
        var orchestrator = new SetupOrchestrator(
            new FakeManifest(), _environment, new VersionGateService(), NullLogger<SetupOrchestrator>.Instance);

        // Both pointed at paths that don't exist, so the tests never read or write the real machine's
        // catalog or the user's saved preferences.
        var formats = new FormatCatalogProvider(Path.Combine(_dir, "no-catalog.json"));

        return new MainViewModel(
            orchestrator, _converter, _environment, new InputResolver(formats), formats, _dialogs,
            UserSettings.Load(Path.Combine(_dir, "settings.json")));
    }

    private string CreateFile(string relativePath)
    {
        var full = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return full;
    }

    private static async Task ConvertAndWait(MainViewModel vm)
    {
        vm.ConvertCommand.Execute(null);
        for (var i = 0; i < 200 && vm.IsBusy; i++) await Task.Delay(10);
        vm.IsBusy.Should().BeFalse("the conversion should have finished by now");
    }

    [Fact]
    public async Task Converting_MarksEveryFileDone()
    {
        var vm = BuildSut();
        vm.AddFiles([CreateFile("a.pdf"), CreateFile("b.docx")]);

        await ConvertAndWait(vm);

        vm.Files.Should().OnlyContain(f => f.Status == FileStatus.Done);
        vm.StatusMessage.Should().Contain("2");
    }

    [Fact]
    public async Task AFileThatFails_DoesNotStopTheRest()
    {
        _converter.FailFor.Add("bad.xlsx");
        var vm = BuildSut();
        vm.AddFiles([CreateFile("a.pdf"), CreateFile("bad.xlsx"), CreateFile("c.docx")]);

        await ConvertAndWait(vm);

        vm.Files.Where(f => f.Status == FileStatus.Done).Should().HaveCount(2);
        vm.Files.Single(f => f.Status == FileStatus.Error).ErrorMessage.Should().NotBeNullOrEmpty();
        vm.StatusMessage.Should().Contain("2/3");
    }

    [Fact]
    public async Task TwoFilesWithTheSameName_DoNotOverwriteEachOther()
    {
        var vm = BuildSut();
        vm.OutputFolder = Path.Combine(_dir, "out");
        vm.AddFiles([CreateFile("2025/report.pdf"), CreateFile("2026/report.pdf")]);

        await ConvertAndWait(vm);

        var written = vm.Files.Select(f => f.OutputPath).ToList();
        written.Should().OnlyHaveUniqueItems();
        vm.StatusMessage.Should().Contain("renamed");
    }

    [Fact]
    public async Task Cancelling_LeavesTheUntouchedFilesPending()
    {
        _converter.PauseBefore = "slow.pdf";
        var vm = BuildSut();
        vm.AddFiles([CreateFile("a.pdf"), CreateFile("slow.pdf"), CreateFile("c.pdf")]);

        vm.ConvertCommand.Execute(null);
        for (var i = 0; i < 200 && !_converter.Paused; i++) await Task.Delay(10);
        vm.CancelCommand.Execute(null);
        _converter.Release();
        for (var i = 0; i < 200 && vm.IsBusy; i++) await Task.Delay(10);

        vm.StatusMessage.Should().Contain("Cancelled");
        vm.Files.Should().Contain(f => f.Status == FileStatus.Pending);
    }

    [Fact]
    public void AddingAPathThatIsNotThere_AddsNothing()
    {
        var vm = BuildSut();

        vm.AddFiles([Path.Combine(_dir, "definitely-not-here")]);

        vm.Files.Should().BeEmpty();
    }

    [Fact]
    public void AddingAFolder_TakesTheConvertibleFilesAndLeavesTheRest()
    {
        var vm = BuildSut();
        CreateFile("drop/report.pdf");
        CreateFile("drop/notes.md");        // would convert onto itself
        CreateFile("drop/program.exe");

        vm.AddFiles([Path.Combine(_dir, "drop")]);

        vm.Files.Select(f => Path.GetFileName(f.SourcePath)).Should().BeEquivalentTo(["report.pdf"]);
    }

    [Fact]
    public void ChoosingAnOutputFolder_RemembersItForNextTime()
    {
        var settingsPath = Path.Combine(_dir, "settings.json");
        var chosen = Path.Combine(_dir, "chosen");
        Directory.CreateDirectory(chosen);
        _dialogs.FolderToReturn = chosen;

        var vm = BuildSut();
        vm.ChooseOutputFolderCommand.Execute(null);

        vm.OutputFolder.Should().Be(chosen);
        File.Exists(settingsPath).Should().BeTrue();
        UserSettings.Load(settingsPath).OutputFolder.Should().Be(chosen);
    }

    [Fact]
    public async Task WhenPythonIsMissing_TheUserIsToldRatherThanLeftGuessing()
    {
        _environment.ThrowOnSetup = new Core.Exceptions.PythonNotFoundException("no python");
        var vm = BuildSut();

        await vm.InitializeAsync();

        vm.IsReady.Should().BeFalse();
        vm.ShowReinstall.Should().BeTrue();
        _dialogs.Messages.Should().ContainSingle().Which.Title.Should().Be("Python missing");
    }

    private sealed class FakeConverter : IMarkItDownConverter
    {
        public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? PauseBefore { get; set; }
        public bool Paused { get; private set; }

        private readonly SemaphoreSlim _gate = new(0);
        public void Release() => _gate.Release();

        public Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConversionResult.Ok("# converted"));

        public async IAsyncEnumerable<ConversionResult> ConvertManyAsync(
            IReadOnlyList<ConversionRequest> requests,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileName(request.SourcePath);
                if (name.Equals(PauseBefore, StringComparison.OrdinalIgnoreCase))
                {
                    Paused = true;
                    await _gate.WaitAsync(CancellationToken.None);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                yield return FailFor.Contains(name)
                    ? ConversionResult.Fail("something went wrong with " + name)
                    : ConversionResult.Ok("# converted", request.OutputPath);
            }
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public List<(string Message, string Title, DialogKind Kind)> Messages { get; } = [];
        public string? FolderToReturn { get; set; }
        public List<string> Opened { get; } = [];

        public void ShowMessage(string message, string title, DialogKind kind) => Messages.Add((message, title, kind));
        public string? PickFolder(string title) => FolderToReturn;
        public void OpenFolder(string path) => Opened.Add(path);
    }

    private sealed class FakeEnvironment : IPythonEnvironmentManager
    {
        public Exception? ThrowOnSetup { get; set; }

        public Task<PythonEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PythonEnvironmentInfo
            {
                IsReady = ThrowOnSetup is null,
                PythonExecutable = @"C:\fake\python.exe",
                InstalledMarkItDownVersion = ThrowOnSetup is null ? "0.1.7" : null,
                MissingReason = ThrowOnSetup is null ? null : "not set up"
            });

        public Task SetupAsync(string markItDownVersion, bool forceReinstall = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            ThrowOnSetup is not null ? Task.FromException(ThrowOnSetup) : Task.CompletedTask;

        public Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("0.1.7");

        public Task EnsureFormatCatalogAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeManifest : IManifestProvider
    {
        public Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompatibilityManifest
            {
                SchemaVersion = 1,
                StableVersion = "0.1.7",
                MinimumVersion = "0.1.7",
                CompatibleVersions = new List<string> { "0.1.7" }.AsReadOnly(),
                UpdatedAt = DateOnly.FromDateTime(DateTime.Today),
                Notes = string.Empty
            });
    }
}
