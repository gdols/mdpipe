using FluentAssertions;
using MdPipe.Core.Models;
using MdPipe.Core.Services;

namespace MdPipe.Core.Tests;

public sealed class AppUpdateServiceTests
{
    private readonly AppUpdateService _sut = new(new VersionGateService());

    private static CompatibilityManifest Announcing(
        string latest, string criticalBelow = "", string notes = "") => new()
    {
        SchemaVersion = 2,
        StableVersion = "0.1.7",
        CompatibleVersions = new List<string> { "0.1.7" }.AsReadOnly(),
        App = new AppRelease(latest, "https://example.invalid/releases/latest", criticalBelow, notes)
    };

    [Fact]
    public void WhenANewerReleaseExists_SaysSo()
    {
        var update = _sut.CheckFor(Announcing("0.5.0"), runningVersion: "0.4.0");

        update.Should().NotBeNull();
        update!.Version.Should().Be("0.5.0");
        update.ReleaseUrl.Should().Be("https://example.invalid/releases/latest");
    }

    [Fact]
    public void WhenRunningTheLatest_SaysNothing()
    {
        _sut.CheckFor(Announcing("0.4.0"), runningVersion: "0.4.0").Should().BeNull();
    }

    [Fact]
    public void WhenRunningSomethingNewerThanTheManifest_SaysNothing()
    {
        // A build straight from master is ahead of the last release, not behind it.
        _sut.CheckFor(Announcing("0.4.0"), runningVersion: "0.5.0").Should().BeNull();
    }

    [Fact]
    public void OnTheOlderSchemaWithNoAppBlock_SaysNothing()
    {
        var manifest = new CompatibilityManifest { SchemaVersion = 1, StableVersion = "0.1.7" };

        _sut.CheckFor(manifest, runningVersion: "0.1.0").Should().BeNull();
    }

    [Fact]
    public void BelowTheCriticalLine_TheNoticeIsMarkedCritical()
    {
        var update = _sut.CheckFor(Announcing("0.5.0", criticalBelow: "0.3.0"), runningVersion: "0.2.0");

        update!.Critical.Should().BeTrue();
    }

    [Fact]
    public void AtOrAboveTheCriticalLine_TheNoticeIsOrdinary()
    {
        var update = _sut.CheckFor(Announcing("0.5.0", criticalBelow: "0.3.0"), runningVersion: "0.3.0");

        update!.Critical.Should().BeFalse();
    }

    [Fact]
    public void WithNoCriticalLine_NothingIsCritical()
    {
        var update = _sut.CheckFor(Announcing("0.5.0"), runningVersion: "0.1.0");

        update!.Critical.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    public void WhenTheRunningVersionMakesNoSense_SaysNothing(string? running)
    {
        // Better to keep quiet than to nag a build whose version we cannot place.
        _sut.CheckFor(Announcing("0.5.0"), running).Should().BeNull();
    }

    [Fact]
    public void WhenTheAdvertisedVersionMakesNoSense_SaysNothing()
    {
        _sut.CheckFor(Announcing("who knows"), runningVersion: "0.4.0").Should().BeNull();
    }

    [Fact]
    public void TheReleaseNotesTravelWithTheNotice()
    {
        var update = _sut.CheckFor(Announcing("0.5.0", notes: "Fixes the folder scan freezing."), "0.4.0");

        update!.Notes.Should().Be("Fixes the folder scan freezing.");
    }
}
