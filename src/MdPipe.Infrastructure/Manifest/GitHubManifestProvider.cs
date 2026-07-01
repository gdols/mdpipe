using System.Text.Json;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Manifest;

/// <summary>
/// Fetches the MarkItDown compatibility manifest from GitHub raw content.
/// The manifest URL is intentionally fixed so only the repo owner can update
/// which MarkItDown versions are considered safe.
/// </summary>
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
        catch (JsonException ex)
        {
            throw new ManifestException($"Manifest JSON is malformed: {ex.Message}", ex);
        }
    }
}
