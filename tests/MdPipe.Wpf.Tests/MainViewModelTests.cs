using System.Globalization;
using System.IO;
using FluentAssertions;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using MdPipe.Wpf;
using MdPipe.Wpf.Resources;
using MdPipe.Wpf.Services;
using MdPipe.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Wpf.Tests;

/// <summary>
/// Covers the batch logic the desktop app runs on. Everything that talks to Windows goes through
/// <see cref="IDialogService"/>, so nothing here can hang on a message box waiting to be dismissed.
/// </summary>
[Collection("ui-strings")]
public sealed class MainViewModelTests : IDisposable
{
    // The status messages are localised now, so the assertions below would follow whatever language
    // the developer's Windows happens to be in. Pin it.
    private readonly CultureInfo? _previousCulture = Strings.Culture;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mdpipe-wpf-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeConverter _converter = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeEnvironment _environment = new();
    private readonly FakeManifest _manifest = new();
    private readonly FakeUpdateInstaller _installer = new();

    public MainViewModelTests()
    {
        Strings.Culture = new CultureInfo("en");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Strings.Culture = _previousCulture;
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>What the build under test claims to be, so update tests can sit either side of it.</summary>
    private const string RunningVersion = "0.4.0";

    private MainViewModel BuildSut()
    {
        var orchestrator = new SetupOrchestrator(
            _manifest, _manifest, _environment, new VersionGateService(), NullLogger<SetupOrchestrator>.Instance);

        // Both pointed at paths that don't exist, so the tests never read or write the real machine's
        // catalog or the user's saved preferences.
        var formats = new FormatCatalogProvider(Path.Combine(_dir, "no-catalog.json"));

        return new MainViewModel(
            orchestrator, _converter, _environment, new InputResolver(formats), formats, _dialogs,
            new AppUpdateService(new VersionGateService()),
            _installer,
            UserSettings.Load(Path.Combine(_dir, "settings.json")),
            RunningVersion);
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
        await vm.AddFilesAsync([CreateFile("a.pdf"), CreateFile("b.docx")]);

        await ConvertAndWait(vm);

        vm.Files.Should().OnlyContain(f => f.Status == FileStatus.Done);
        vm.StatusMessage.Should().Contain("2");
    }

    [Fact]
    public async Task AFileThatFails_DoesNotStopTheRest()
    {
        _converter.FailFor.Add("bad.xlsx");
        var vm = BuildSut();
        await vm.AddFilesAsync([CreateFile("a.pdf"), CreateFile("bad.xlsx"), CreateFile("c.docx")]);

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
        await vm.AddFilesAsync([CreateFile("2025/report.pdf"), CreateFile("2026/report.pdf")]);

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
        await vm.AddFilesAsync([CreateFile("a.pdf"), CreateFile("slow.pdf"), CreateFile("c.pdf")]);

        vm.ConvertCommand.Execute(null);
        for (var i = 0; i < 200 && !_converter.Paused; i++) await Task.Delay(10);
        vm.CancelCommand.Execute(null);
        _converter.Release();
        for (var i = 0; i < 200 && vm.IsBusy; i++) await Task.Delay(10);

        vm.StatusMessage.Should().Contain("Cancelled");
        vm.Files.Should().Contain(f => f.Status == FileStatus.Pending);
    }

    [Fact]
    public async Task TheWindowHandsTheReleaseItLearnedAboutToTheNotice()
    {
        // The notice looks after itself and is tested on its own; what belongs here is the wiring,
        // that the answer coming back from setup actually reaches it.
        _manifest.App = new AppRelease("9.9.9", "https://example.invalid/r");
        var vm = BuildSut();

        await vm.InitializeAsync();

        vm.UpdateNotice.HasNotice.Should().BeTrue();
        vm.UpdateNotice.Update!.Version.Should().Be("9.9.9");
    }

    [Fact]
    public async Task FilesDroppedWhileTheEnvironmentPrepares_StillGetAdded()
    {
        // The first run spends minutes downloading Python and MarkItDown. A drop during that used to
        // hit an IsBusy guard and vanish without a message, which is the worst possible answer.
        _environment.Gate = new TaskCompletionSource();
        var vm = BuildSut();
        var preparing = vm.InitializeAsync();
        vm.IsBusy.Should().BeTrue("the environment is still being prepared");

        await vm.AddFilesAsync([CreateFile("dropped.pdf")]);

        vm.Files.Select(f => Path.GetFileName(f.SourcePath)).Should().BeEquivalentTo(["dropped.pdf"]);

        _environment.Gate.SetResult();
        await preparing;
    }

    [Fact]
    public async Task FilesDroppedDuringAConversion_AreIgnored()
    {
        // The one case that still has to be refused: the batch already knows what it is converting.
        _converter.PauseBefore = "slow.pdf";
        var vm = BuildSut();
        await vm.AddFilesAsync([CreateFile("slow.pdf")]);
        vm.ConvertCommand.Execute(null);
        for (var i = 0; i < 200 && !_converter.Paused; i++) await Task.Delay(10);

        await vm.AddFilesAsync([CreateFile("late.pdf")]);

        vm.Files.Should().NotContain(f => Path.GetFileName(f.SourcePath) == "late.pdf");

        _converter.Release();
        for (var i = 0; i < 200 && vm.IsBusy; i++) await Task.Delay(10);
    }

    [Fact]
    public async Task AddingAPathThatIsNotThere_AddsNothing()
    {
        var vm = BuildSut();

        await vm.AddFilesAsync([Path.Combine(_dir, "definitely-not-here")]);

        vm.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task AddingAFolder_TakesTheConvertibleFilesAndLeavesTheRest()
    {
        var vm = BuildSut();
        CreateFile("drop/report.pdf");
        CreateFile("drop/notes.md");        // would convert onto itself
        CreateFile("drop/program.exe");

        await vm.AddFilesAsync([Path.Combine(_dir, "drop")]);

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

        // Only the update notice asks these, and it is tested where it lives now.
        public bool Confirm(string message, string title) => false;
        public void OpenLink(string url) { }
        public void RestartWith(string executablePath) { }
    }

    /// <summary>Required to build the view model; the notice that uses it is tested separately.</summary>
    private sealed class FakeUpdateInstaller : IAppUpdateInstaller
    {
        public bool CanInstall => false;

        public Task<string> InstallAsync(
            AppUpdate update, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The update flow is covered by UpdateNoticeViewModelTests.");

        public void CleanUpPreviousUpdate() { }
    }

    private sealed class FakeEnvironment : IPythonEnvironmentManager
    {
        public Exception? ThrowOnSetup { get; set; }

        /// <summary>Holds the environment check open so a test can act while the app is busy.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task<PythonEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default)
        {
            if (Gate is not null) await Gate.Task;

            return new PythonEnvironmentInfo
            {
                IsReady = ThrowOnSetup is null,
                PythonExecutable = @"C:\fake\python.exe",
                InstalledMarkItDownVersion = ThrowOnSetup is null ? "0.1.7" : null,
                MissingReason = ThrowOnSetup is null ? null : "not set up"
            };
        }

        public Task SetupAsync(string markItDownVersion, bool forceReinstall = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            ThrowOnSetup is not null ? Task.FromException(ThrowOnSetup) : Task.CompletedTask;

        public Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("0.1.7");

        public Task EnsureFormatCatalogAsync(string? installedVersion = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeManifest : IBuildManifestProvider
    {
        /// <summary>What the manifest claims the newest MdPipe is, or null for the older schema.</summary>
        public AppRelease? App { get; set; }

        public Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompatibilityManifest
            {
                SchemaVersion = App is null ? 1 : 2,
                StableVersion = "0.1.7",
                MinimumVersion = "0.1.7",
                CompatibleVersions = new List<string> { "0.1.7" }.AsReadOnly(),
                UpdatedAt = DateOnly.FromDateTime(DateTime.Today),
                Notes = string.Empty,
                App = App
            });
    }
}
