using FluentAssertions;
using MdPipe.Infrastructure.Python;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// The parts of the environment manager that do not need to start anything.
/// </summary>
/// <remarks>
/// This is the file every field failure has come out of: the Microsoft Store Python that reports a
/// version and refuses to build a virtual environment, an install whose standard library had been
/// moved, a rebuild that silently kept the old environment because the embeddable zip extracts some
/// files read-only, a proxy that was detected and never handed to pip. It was also the only class in
/// the project with no tests at all, because everything in it was reached through a static path into
/// the real AppData.
/// <para>
/// The folder is now a constructor argument, so what follows runs against a throwaway directory.
/// Finding a working interpreter still needs real processes and is still not covered here; what is
/// covered is the file handling that quietly went wrong on other people's machines.
/// </para>
/// </remarks>
public sealed class PythonEnvironmentManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mdpipe-env-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception) { }
    }

    private PythonEnvironmentManager Sut() =>
        new(NullLogger<PythonEnvironmentManager>.Instance, new NoHttp(), _root);

    private string Touch(string relativePath, string content = "")
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // --- which interpreter gets used -------------------------------------------------------

    [Fact]
    public void WithNothingInstalled_ThereIsNoInterpreter()
    {
        Sut().PythonExecutable.Should().BeNull();
    }

    [Fact]
    public void AVirtualEnvironmentIsPreferredOverTheEmbeddedOne()
    {
        // The order matters. A virtual environment exists only when a healthy system Python was
        // found, and building on that is cheaper and better behaved than the downloaded embeddable.
        var venv = Touch(@"venv\Scripts\python.exe");
        Touch(@"python\python.exe");

        Sut().PythonExecutable.Should().Be(venv);
    }

    [Fact]
    public void TheEmbeddedOneIsUsedWhenThereIsNoVirtualEnvironment()
    {
        var embedded = Touch(@"python\python.exe");

        Sut().PythonExecutable.Should().Be(embedded);
    }

    [Fact]
    public async Task WithNoInterpreter_TheEnvironmentReportsWhyRatherThanThrowing()
    {
        var info = await Sut().GetEnvironmentInfoAsync();

        info.IsReady.Should().BeFalse();
        info.MissingReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WithNoInterpreter_AskingForTheFormatsDoesNothingQuietly()
    {
        // Called on every setup path, including ones that failed before installing anything.
        var act = () => Sut().EnsureFormatCatalogAsync();

        await act.Should().NotThrowAsync();
        File.Exists(Path.Combine(_root, "formats.json")).Should().BeFalse();
    }

    // --- the worker script ------------------------------------------------------------------

    [Fact]
    public void TheWorkerScriptIsWrittenWhereTheInterpreterCanReachIt()
    {
        var path = Sut().EnsureWorkerScript();

        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).Should().Contain("MdPipe conversion worker");
    }

    [Fact]
    public void AStaleWorkerScriptIsReplaced()
    {
        // An MdPipe that updated itself must not go on talking to the script the old one left
        // behind, which is a different protocol as far as anyone knows.
        var path = Sut().EnsureWorkerScript();
        File.WriteAllText(path, "print('a script from an older MdPipe')");

        Sut().EnsureWorkerScript();

        File.ReadAllText(path).Should().Contain("MdPipe conversion worker");
    }

    [Fact]
    public void AWorkerScriptThatIsAlreadyCurrentIsLeftAlone()
    {
        var path = Sut().EnsureWorkerScript();
        var written = File.GetLastWriteTimeUtc(path);

        Thread.Sleep(20);
        Sut().EnsureWorkerScript();

        File.GetLastWriteTimeUtc(path).Should().Be(written);
    }

    // --- the embeddable Python's path file ---------------------------------------------------

    [Fact]
    public void TheEmbeddedPythonIsToldToLoadItsPackages()
    {
        // The embeddable ships with "import site" commented out and no site-packages entry, which
        // means pip installs into a folder the interpreter then refuses to look in.
        var pth = Touch(@"python\python312._pth", "python312.zip\n.\n\n#import site\n");

        Sut().EnableEmbeddedSitePackages();

        var lines = File.ReadAllLines(pth);
        lines.Should().Contain("import site");
        lines.Should().NotContain("#import site");
        lines.Should().Contain(@"Lib\site-packages");
    }

    [Fact]
    public void RunningItTwiceChangesNothingTheSecondTime()
    {
        var pth = Touch(@"python\python312._pth", "python312.zip\n.\n\n#import site\n");
        Sut().EnableEmbeddedSitePackages();
        var after = File.ReadAllText(pth);

        Sut().EnableEmbeddedSitePackages();

        File.ReadAllText(pth).Should().Be(after, "a repeated setup should not keep appending lines");
        File.ReadAllLines(pth).Count(l => l.Trim() == "import site").Should().Be(1);
    }

    [Fact]
    public void APathFileThatAlreadyLooksRightGainsNothing()
    {
        // Compared as lines rather than as text: the file is rewritten either way, which normalises
        // the line endings, and that is not what this is about. What matters is that a second entry
        // is not appended to a file that already had one.
        var pth = Touch(@"python\python312._pth", "python312.zip\n.\nLib\\site-packages\nimport site\n");
        var before = File.ReadAllLines(pth);

        Sut().EnableEmbeddedSitePackages();

        File.ReadAllLines(pth).Should().Equal(before);
    }

    [Fact]
    public void NoPathFileAtAll_IsReportedRatherThanThrown()
    {
        Directory.CreateDirectory(Path.Combine(_root, "python"));

        var act = () => Sut().EnableEmbeddedSitePackages();

        act.Should().NotThrow("a missing ._pth is a broken download, not a reason to crash setup");
    }

    // --- deleting an environment before rebuilding it -----------------------------------------

    [Fact]
    public void ReadOnlyFilesDoNotStopAnEnvironmentBeingRemoved()
    {
        // The bug this exists for: the embeddable zip extracts some files read-only, Directory.Delete
        // refuses to touch those, and "reinstall" silently kept the broken environment it was meant
        // to replace. Nothing said so, and the reinstall appeared to work.
        var stubborn = Touch(@"python\Lib\stubborn.pyd", "read only");
        File.SetAttributes(stubborn, FileAttributes.ReadOnly);

        Sut().TryDeleteDir(Path.Combine(_root, "python"));

        Directory.Exists(Path.Combine(_root, "python")).Should().BeFalse();
    }

    [Fact]
    public void DeletingSomethingThatIsNotThereIsFine()
    {
        var act = () => Sut().TryDeleteDir(Path.Combine(_root, "never-existed"));

        act.Should().NotThrow();
    }

    [Fact]
    public void ALockedFileLeavesTheRestAloneRatherThanThrowing()
    {
        // Antivirus and Explorer both hold files open at inconvenient moments. Setup carries on and
        // says so in the log instead of failing outright.
        var locked = Touch(@"python\held-open.dll", "x");
        using var handle = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var act = () => Sut().TryDeleteDir(Path.Combine(_root, "python"));

        act.Should().NotThrow();
    }

    /// <summary>Nothing here reaches the network; asking for a client would be a bug in the test.</summary>
    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("These tests should not be downloading anything.");
    }
}
