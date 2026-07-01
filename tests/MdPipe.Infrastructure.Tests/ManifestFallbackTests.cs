using FluentAssertions;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Infrastructure.Manifest;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MdPipe.Infrastructure.Tests;

public class ManifestFallbackTests
{
    [Fact]
    public async Task EmbeddedManifestProvider_ReadsBakedInBaseline()
    {
        var sut = new EmbeddedManifestProvider();

        var manifest = await sut.GetManifestAsync();

        manifest.StableVersion.Should().NotBeNullOrWhiteSpace();
        manifest.CompatibleVersions.Should().NotBeEmpty();
        manifest.CompatibleVersions.Should().Contain(manifest.StableVersion);
    }

    [Fact]
    public async Task Fallback_WhenPrimaryFails_UsesFallback()
    {
        var primary = Substitute.For<IManifestProvider>();
        primary.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns<CompatibilityManifest>(_ => throw new ManifestException("remote 404"));

        var sut = new FallbackManifestProvider(
            primary,
            new EmbeddedManifestProvider(),
            NullLogger<FallbackManifestProvider>.Instance);

        var manifest = await sut.GetManifestAsync();

        manifest.StableVersion.Should().NotBeNullOrWhiteSpace();
        await primary.Received(1).GetManifestAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_WhenPrimarySucceeds_DoesNotUseFallback()
    {
        var remote = new CompatibilityManifest { StableVersion = "9.9.9", CompatibleVersions = new[] { "9.9.9" } };
        var primary = Substitute.For<IManifestProvider>();
        primary.GetManifestAsync(Arg.Any<CancellationToken>()).Returns(remote);

        var fallback = Substitute.For<IManifestProvider>();

        var sut = new FallbackManifestProvider(primary, fallback, NullLogger<FallbackManifestProvider>.Instance);

        var manifest = await sut.GetManifestAsync();

        manifest.StableVersion.Should().Be("9.9.9");
        await fallback.DidNotReceive().GetManifestAsync(Arg.Any<CancellationToken>());
    }
}
