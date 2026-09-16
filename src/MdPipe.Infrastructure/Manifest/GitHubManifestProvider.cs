using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Manifest;

public sealed class GitHubManifestProvider(
    HttpClient httpClient,
    ILogger<GitHubManifestProvider> logger) : IManifestProvider
{
    public async Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching compatibility manifest from GitHub");
            var json = await httpClient.GetStringAsync(string.Empty, cancellationToken);
            return ManifestSerializer.Deserialize(json);
        }
        catch (HttpRequestException ex)
        {
            throw new ManifestException($"Failed to fetch manifest: {ex.Message}", ex);
        }
        // HttpClient reports its own timeout as a cancellation, not a request failure. Without
        // this, a proxy that accepts the connection and then says nothing sends a
        // TaskCanceledException straight past the fallback chain. Real cancellation still propagates.
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ManifestException(
                $"Timed out fetching the manifest after {httpClient.Timeout.TotalSeconds:0} seconds.", ex);
        }
    }
}
