using FluentAssertions;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Core.Tests;

public class SetupOrchestratorTests
{
    private readonly FakeEnvironmentManager _environment = new();

    /// <summary>The release this build claims to be paired with, which is what decides everything.</summary>
    private SetupOrchestrator BuildSut(CompatibilityManifest build, AppRelease? announced = null) => new(
        new FakeBuildManifest(build),
        new FakeManifestProvider(new CompatibilityManifest { App = announced }),
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
    public async Task RunAsync_WhenTheEngineIsNotTheOneThisBuildPins_ReplacesIt()
    {
        // An engine nobody can identify is not the one this release was tested against, so it goes.
        _environment.Info = Ready("weird-build");

        var result = await BuildSut(BuildManifest("0.1.7", "weird-build", "0.1.7")).RunAsync();

        result.WasInstalled.Should().BeTrue();
        result.Version.Should().Be("0.1.7");
        _environment.SetupCalls.Should().ContainSingle().Which.Version.Should().Be("0.1.7");
    }

    [Fact]
    public async Task RunAsync_WhenTheEngineReportsItselfSlightlyDifferently_LeavesItAlone()
    {
        // "0.1.7" pinned and "0.1.7.0" reported is the same engine. Treating it as a mismatch would
        // reinstall several hundred megabytes on every single launch, forever.
        _environment.Info = Ready("0.1.7.0");

        var result = await BuildSut(BuildManifest("0.1.7", "0.1.7")).RunAsync();

        result.WasInstalled.Should().BeFalse();
        _environment.SetupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TwiceInARow_OnlyInstallsOnce()
    {
        // The guard against the loop above, from the other side: once the pinned engine is in, a
        // second launch has nothing to do.
        _environment.Info = new PythonEnvironmentInfo { IsReady = false, MissingReason = "not set up" };
        var sut = BuildSut(BuildManifest("0.1.7", "0.1.7"));

        await sut.RunAsync();
        var second = await sut.RunAsync();

        second.WasInstalled.Should().BeFalse();
        _environment.SetupCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_IgnoresWhatTheRepositoryWantsForTheEngine()
    {
        // The point of the whole arrangement. Editing the manifest in the repository must not be
        // able to swap the engine underneath somebody: a release is an application and a MarkItDown
        // that were tried together, and only a new release changes that pairing.
        _environment.Info = Ready("0.1.7");

        var sut = new SetupOrchestrator(
            new FakeBuildManifest(BuildManifest("0.1.7", "0.1.7")),
            new FakeManifestProvider(BuildManifest("0.1.5", "0.1.5")),
            _environment,
            new VersionGateService(),
            NullLogger<SetupOrchestrator>.Instance);

        var result = await sut.RunAsync();

        result.Version.Should().Be("0.1.7");
        _environment.SetupCalls.Should().BeEmpty("the repository does not get a say in this");
    }

    [Fact]
    public async Task RunAsync_WhenTheRepositoryIsUnreachable_StillSetsUpTheEngine()
    {
        // The remote manifest is now advisory, so losing it costs the update notice and nothing else.
        // It used to decide what got installed, which made a broken network a broken setup.
        _environment.Info = new PythonEnvironmentInfo { IsReady = false, MissingReason = "not set up" };

        var sut = new SetupOrchestrator(
            new FakeBuildManifest(BuildManifest("0.1.7", "0.1.7")),
            new UnreachableManifest(),
            _environment,
            new VersionGateService(),
            NullLogger<SetupOrchestrator>.Instance);

        var result = await sut.RunAsync();

        result.WasInstalled.Should().BeTrue();
        result.Version.Should().Be("0.1.7");
        result.App.Should().BeNull("there was nowhere to learn about a newer MdPipe");
    }

    [Fact]
    public async Task RunAsync_HandsBackWhatTheRepositorySaysAboutNewerReleases()
    {
        _environment.Info = Ready("0.1.7");
        var announced = new AppRelease("9.9.9", "https://example.invalid/r");

        var result = await BuildSut(BuildManifest("0.1.7", "0.1.7"), announced).RunAsync();

        result.App.Should().Be(announced);
    }

    [Fact]
    public async Task RunAsync_WhenNothingNeedsInstalling_StillChecksTheFormatCatalog()
    {
        // The catalog used to be written only as part of an install, so an environment that was
        // already up to date never got one and the app fell back to the bundled list forever.
        _environment.Info = Ready("0.1.7");

        await BuildSut(BuildManifest("0.1.7", "0.1.7")).RunAsync();

        _environment.SetupCalls.Should().BeEmpty();
        _environment.FormatCatalogChecks.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_HandsOverTheVersionItAlreadyLookedUp()
    {
        // Looking it up costs an interpreter start. Doing it twice on every launch, to get the same
        // answer, was most of the wait before the window said "Ready".
        _environment.Info = Ready("0.1.7");

        await BuildSut(BuildManifest("0.1.7", "0.1.7")).RunAsync();

        _environment.FormatCatalogVersion.Should().Be("0.1.7");
    }

    [Fact]
    public async Task RunAsync_AfterInstalling_HandsOverTheVersionItInstalled()
    {
        _environment.Info = new PythonEnvironmentInfo { IsReady = false, MissingReason = "not set up" };

        await BuildSut(BuildManifest("0.1.7", "0.1.7")).RunAsync();

        _environment.FormatCatalogVersion.Should().Be("0.1.7");
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

    /// <summary>Stands in for GitHub being unreachable.</summary>
    private sealed class UnreachableManifest : IManifestProvider
    {
        public Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
            throw new ManifestException("no route to host");
    }

    private sealed class FakeBuildManifest(CompatibilityManifest manifest) : IBuildManifestProvider
    {
        public Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);
    }

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

            // Installing actually changes what is installed. Without this the fake would happily let
            // a reinstall loop pass its tests, which is the one bug worth catching here.
            Info = new PythonEnvironmentInfo
            {
                IsReady = true,
                PythonExecutable = @"C:\fake\python.exe",
                InstalledMarkItDownVersion = markItDownVersion
            };
            return Task.CompletedTask;
        }

        public Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Info.InstalledMarkItDownVersion);

        public int FormatCatalogChecks { get; private set; }

        /// <summary>The version the orchestrator handed over, or null if it made this look it up.</summary>
        public string? FormatCatalogVersion { get; private set; }

        public Task EnsureFormatCatalogAsync(string? installedVersion = null, CancellationToken cancellationToken = default)
        {
            FormatCatalogChecks++;
            FormatCatalogVersion = installedVersion;
            return Task.CompletedTask;
        }
    }

    /// <summary>Progress&lt;T&gt; posts to a sync context; this one invokes inline so tests can assert right away.</summary>
    private sealed class SynchronousProgress(Action<string> handler) : IProgress<string>
    {
        public void Report(string value) => handler(value);
    }
}
