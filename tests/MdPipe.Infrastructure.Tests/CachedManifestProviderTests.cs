using FluentAssertions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Infrastructure.Manifest;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MdPipe.Infrastructure.Tests;

public class CachedManifestProviderTests : IDisposable
{
    private readonly IManifestProvider _inner = Substitute.For<IManifestProvider>();
    private readonly string _cachePath = Path.Combine(
        Path.GetTempPath(), $"mdpipe-test-{Guid.NewGuid():N}", "manifest-cache.json");

    private CachedManifestProvider CreateSut() =>
        new(_inner, NullLogger<CachedManifestProvider>.Instance, _cachePath);

    private static CompatibilityManifest SampleManifest() => new()
    {
        SchemaVersion = 1,
        StableVersion = "0.1.1",
        MinimumVersion = "0.1.0",
        CompatibleVersions = new List<string> { "0.1.0", "0.1.1" }.AsReadOnly(),
        UpdatedAt = DateOnly.FromDateTime(DateTime.Today),
        Notes = "Test"
    };

    [Fact]
    public async Task GetManifestAsync_WhenNoCacheExists_FetchesFromInner()
    {
        _inner.GetManifestAsync(Arg.Any<CancellationToken>()).Returns(SampleManifest());

        var result = await CreateSut().GetManifestAsync();

        result.StableVersion.Should().Be("0.1.1");
        await _inner.Received(1).GetManifestAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetManifestAsync_WhenFreshCacheExists_DoesNotCallInner()
    {
        _inner.GetManifestAsync(Arg.Any<CancellationToken>()).Returns(SampleManifest());

        // First call populates the cache, second should read it.
        await CreateSut().GetManifestAsync();
        var result = await CreateSut().GetManifestAsync();

        result.StableVersion.Should().Be("0.1.1");
        await _inner.Received(1).GetManifestAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetManifestAsync_WhenInnerThrowsAndNoCache_PropagatesException()
    {
        _inner.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns<CompatibilityManifest>(_ => throw new HttpRequestException("timeout"));

        var act = () => CreateSut().GetManifestAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_cachePath);
        if (dir is not null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
