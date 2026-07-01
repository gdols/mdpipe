using FluentAssertions;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Models;
using MdPipe.Core.Services;

namespace MdPipe.Core.Tests;

public class VersionGateServiceTests
{
    private readonly VersionGateService _sut = new();

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
    public void IsCompatible_WithListedVersion_ReturnsTrue()
    {
        var manifest = BuildManifest("0.1.1", "0.1.0", "0.1.1");

        _sut.IsCompatible("0.1.1", manifest).Should().BeTrue();
        _sut.IsCompatible("0.1.0", manifest).Should().BeTrue();
    }

    [Fact]
    public void IsCompatible_WithUnlistedVersion_ReturnsFalse()
    {
        var manifest = BuildManifest("0.1.1", "0.1.0", "0.1.1");

        _sut.IsCompatible("0.2.0", manifest).Should().BeFalse();
    }

    [Fact]
    public void IsCompatible_WithEmptyVersion_ReturnsFalse()
    {
        var manifest = BuildManifest("0.1.1", "0.1.1");

        _sut.IsCompatible(string.Empty, manifest).Should().BeFalse();
    }

    [Fact]
    public void ThrowIfIncompatible_WithIncompatibleVersion_Throws()
    {
        var manifest = BuildManifest("0.1.1", "0.1.1");

        var act = () => _sut.ThrowIfIncompatible("0.2.0", manifest);

        act.Should().Throw<VersionGateException>()
            .Which.InstalledVersion.Should().Be("0.2.0");
    }

    [Fact]
    public void ThrowIfIncompatible_WithCompatibleVersion_DoesNotThrow()
    {
        var manifest = BuildManifest("0.1.1", "0.1.1");

        var act = () => _sut.ThrowIfIncompatible("0.1.1", manifest);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetTargetVersion_ReturnsStableVersion()
    {
        var manifest = BuildManifest("0.1.1", "0.1.0", "0.1.1");

        _sut.GetTargetVersion(manifest).Should().Be("0.1.1");
    }

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("0.1.1", true)]
    [InlineData("0.2.0", false)]
    [InlineData("0.0.9", false)]
    public void IsCompatible_TheoryData(string version, bool expected)
    {
        var manifest = BuildManifest("0.1.1", "0.1.0", "0.1.1");

        _sut.IsCompatible(version, manifest).Should().Be(expected);
    }
}
