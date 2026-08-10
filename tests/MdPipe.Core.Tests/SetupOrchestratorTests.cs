using FluentAssertions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Core.Tests;

public class SetupOrchestratorTests
{
    private readonly FakeEnvironmentManager _environment = new();

    private SetupOrchestrator BuildSut(CompatibilityManifest manifest) => new(
        new FakeManifestProvider(manifest),
        _environment,
        new VersionGateService(),
        NullLogger<SetupOrchestrator>.Instance);

    private static CompatibilityManifest BuildManifest(string stable, params string[] compatible) => new()
    {
        SchemaVersion = 1,
        StableVersion = stable,
        MinimumVersion = compatible.FirstOrDefault() ?? stable,
        CompatibleVersions = compatible.ToList().AsReadOnly(),
        UpdatedAt = DateOnly.FromDateTime(DateTime.Today),
        Notes = string.Empty
    };

    [Fact]
    public async Task RunAsync_WhenInstalledIsTheStable_DoesNothing()
    {
        _environment.Info = Ready("0.1.7");

        var result = await BuildSut(BuildManifest("0.1.7", "0.1.6", "0.1.7")).RunAsync();

        result.WasInstalled.Should().BeFalse();
        result.Version.Should().Be("0.1.7");
        _environment.SetupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenACompatibleButOlderVersionIsInstalled_UpgradesToStable()
    {
        _environment.Info = Ready("0.1.6");

        var result = await BuildSut(BuildManifest("0.1.7", "0.1.6", "0.1.7")).RunAsync();

        result.WasInstalled.Should().BeTrue();
        result.Version.Should().Be("0.1.7");
        _environment.SetupCalls.Should().ContainSingle()
            .Which.Should().Be(("0.1.7", false));
    }

    [Fact]
    public async Task RunAsync_WhenInstalledVersionFellOutOfTheWindow_UpgradesToStable()
    {
        _environment.Info = Ready("0.1.4");

        var result = await BuildSut(BuildManifest("0.1.7", "0.1.5", "0.1.6", "0.1.7")).RunAsync();

        result.WasInstalled.Should().BeTrue();
        _environment.SetupCalls.Should().ContainSingle()
            .Which.Should().Be(("0.1.7", false));
    }

    [Fact]
    public async Task RunAsync_WhenNothingIsInstalled_InstallsStable()
    {
        _environment.Info = new PythonEnvironmentInfo { IsReady = false, MissingReason = "not set up" };

        var result = await BuildSut(BuildManifest("0.1.7", "0.1.7")).RunAsync();

        result.WasInstalled.Should().BeTrue();
        result.Version.Should().Be("0.1.7");
    }

    [Fact]
    public async Task RunAsync_WithForceReinstall_ReinstallsEvenWhenUpToDate()
    {
        _environment.Info = Ready("0.1.7");

        var result = await BuildSut(BuildManifest("0.1.7", "0.1.7")).RunAsync(forceReinstall: true);

        result.WasInstalled.Should().BeTrue();
        _environment.SetupCalls.Should().ContainSingle()
            .Which.Should().Be(("0.1.7", true));
    }

    [Fact]
    public async Task RunAsync_WhenAPreReleaseIsInstalledAndTheFinalIsStable_Upgrades()
    {
        // Regression for the PyPI-style version gap: 0.1.5b1 must count as older than 0.1.5.
        _environment.Info = Ready("0.1.5b1");

        var result = await BuildSut(BuildManifest("0.1.5", "0.1.5b1", "0.1.5")).RunAsync();

        result.WasInstalled.Should().BeTrue();
        result.Version.Should().Be("0.1.5");
    }

    [Fact]
    public async Task RunAsync_WhenAPostReleaseBecomesStable_Upgrades()
    {
        _environment.Info = Ready("0.1.7");

        var result = await BuildSut(BuildManifest("0.1.7.post1", "0.1.7", "0.1.7.post1")).RunAsync();

        result.WasInstalled.Should().BeTrue();
        result.Version.Should().Be("0.1.7.post1");
    }

    [Fact]
    public async Task RunAsync_WhenVersionsCannotBeCompared_StaysOnTheInstalledOne()
    {
        // If we can't reason about the versions, a compatible install should be left alone
        // (and the orchestrator logs a warning) rather than reinstalled on every launch.
        _environment.Info = Ready("weird-build");

        var result = await BuildSut(BuildManifest("0.1.7", "weird-build", "0.1.7")).RunAsync();

        result.WasInstalled.Should().BeFalse();
        result.Version.Should().Be("weird-build");
        _environment.SetupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ReportsProgressAlongTheWay()
    {
        _environment.Info = Ready("0.1.6");
        var messages = new List<string>();
        var progress = new SynchronousProgress(messages.Add);

        await BuildSut(BuildManifest("0.1.7", "0.1.6", "0.1.7")).RunAsync(progress: progress);

        messages.Should().Contain(m => m.Contains("0.1.7"));
    }

    private static PythonEnvironmentInfo Ready(string version) => new()
    {
        IsReady = true,
        PythonExecutable = @"C:\fake\python.exe",
        InstalledMarkItDownVersion = version
    };

    private sealed class FakeManifestProvider(CompatibilityManifest manifest) : IManifestProvider
    {
        public Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);
    }

    private sealed class FakeEnvironmentManager : IPythonEnvironmentManager
    {
        public PythonEnvironmentInfo Info { get; set; } = new();
        public List<(string Version, bool Force)> SetupCalls { get; } = [];

        public Task<PythonEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Info);

        public Task SetupAsync(string markItDownVersion, bool forceReinstall = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            SetupCalls.Add((markItDownVersion, forceReinstall));
            return Task.CompletedTask;
        }

        public Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Info.InstalledMarkItDownVersion);
    }

    /// <summary>Progress&lt;T&gt; posts to a sync context; this one invokes inline so tests can assert right away.</summary>
    private sealed class SynchronousProgress(Action<string> handler) : IProgress<string>
    {
        public void Report(string value) => handler(value);
    }
}
