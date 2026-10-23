using FluentAssertions;
using MdPipe.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Tests;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mdpipe-log-tests", Guid.NewGuid().ToString("N"));

    public FileLoggerProviderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    private string LogPath => Path.Combine(_dir, "mdpipe.log");

    [Fact]
    public void WritesTheLevelTheSourceTheMessageAndTheException()
    {
        using var provider = new FileLoggerProvider(LogPath);
        var logger = provider.CreateLogger("MdPipe.Infrastructure.Python.PythonEnvironmentManager");

        logger.LogWarning(new InvalidOperationException("pip gave up"), "Setup failed for {Version}", "0.1.9");

        var text = File.ReadAllText(LogPath);
        text.Should().Contain("WARN  PythonEnvironmentManager: Setup failed for 0.1.9");
        text.Should().Contain("InvalidOperationException: pip gave up");
    }

    [Fact]
    public void LeavesOutWhatIsBelowTheMinimum()
    {
        using var provider = new FileLoggerProvider(LogPath, LogLevel.Information);
        var logger = provider.CreateLogger("Test");

        logger.LogDebug("noise");
        logger.LogInformation("kept");

        File.ReadAllText(LogPath).Should().Contain("kept").And.NotContain("noise");
    }

    [Fact]
    public void AFileThatGrewTooBig_IsSetAsideAtStartup()
    {
        File.WriteAllText(LogPath, new string('x', (int)FileLoggerProvider.MaxBytes + 10));

        using var provider = new FileLoggerProvider(LogPath);
        provider.CreateLogger("Test").LogInformation("fresh");

        new FileInfo(Path.Combine(_dir, "mdpipe.old.log")).Length.Should().BeGreaterThan(FileLoggerProvider.MaxBytes);
        File.ReadAllText(LogPath).Should().Contain("fresh").And.NotContain("xxxx");
    }

    [Fact]
    public void ALogItCannotWrite_NeverThrows()
    {
        // A folder where the file should be: every write fails, and none of it may reach the app.
        Directory.CreateDirectory(LogPath);

        var act = () =>
        {
            using var provider = new FileLoggerProvider(LogPath);
            provider.CreateLogger("Test").LogError("lost");
        };

        act.Should().NotThrow();
    }
}
