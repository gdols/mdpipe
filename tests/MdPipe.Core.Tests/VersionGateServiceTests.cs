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

    [Theory]
    [InlineData("0.1.7", "0.1.6", 1)]
    [InlineData("0.1.6", "0.1.7", -1)]
    [InlineData("0.1.7", "0.1.7", 0)]
    [InlineData("0.1", "0.1.0", 0)]        // shorter release pads with zeros
    [InlineData("0.1.10", "0.1.9", 1)]     // numeric, not lexicographic
    [InlineData("v0.1.7", "0.1.7", 0)]     // tolerated "v" prefix
    public void Compare_PlainReleases_OrdersNumerically(string left, string right, int expectedSign)
    {
        var result = _sut.Compare(left, right);

        result.Should().NotBeNull();
        Math.Sign(result!.Value).Should().Be(expectedSign);
    }

    [Theory]
    [InlineData("0.1.5", "0.1.5b1", 1)]         // final beats pre-release
    [InlineData("0.1.5b1", "0.1.5a2", 1)]       // b beats a
    [InlineData("0.1.5rc1", "0.1.5b9", 1)]      // rc beats b
    [InlineData("0.1.5b2", "0.1.5b1", 1)]       // same phase, higher number wins
    [InlineData("0.1.7.post1", "0.1.7", 1)]     // post beats final
    [InlineData("0.1.7post1", "0.1.7", 1)]      // post without the dot, same thing
    [InlineData("1.0.0rc1", "1.0.0", -1)]       // the exact shape from the issue
    [InlineData("0.2.0a1", "0.1.9", 1)]         // pre-release of a newer release still wins
    public void Compare_PyPiStyleVersions_FollowsPep440Ordering(string left, string right, int expectedSign)
    {
        var result = _sut.Compare(left, right);

        result.Should().NotBeNull();
        Math.Sign(result!.Value).Should().Be(expectedSign);
    }

    [Theory]
    [InlineData("garbage", "0.1.7")]
    [InlineData("0.1.7", "")]
    [InlineData("0.1.7", "   ")]
    [InlineData("1.2.x", "0.1.7")]
    [InlineData("0.1.5b1.post2", "0.1.7")]  // pre+post combined is out of scope, better null than wrong
    public void Compare_WithUnparseableInput_ReturnsNull(string left, string right)
    {
        _sut.Compare(left, right).Should().BeNull();
    }
}
