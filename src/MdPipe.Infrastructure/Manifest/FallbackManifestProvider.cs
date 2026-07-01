using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Manifest;

/// <summary>
/// Tries the primary provider (remote + cache) and, if it fails, falls back to a
/// secondary provider (the embedded baseline). This keeps MdPipe usable offline or
/// before the remote manifest is published, while still preferring the remote one.
/// </summary>
public sealed class FallbackManifestProvider(
    IManifestProvider primary,
    IManifestProvider fallback,
    ILogger<FallbackManifestProvider> logger) : IManifestProvider
{
    public async Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await primary.GetManifestAsync(cancellationToken);
        }
        catch (ManifestException ex)
        {
            logger.LogWarning("Remote manifest unavailable ({Reason}). Using the bundled baseline manifest.", ex.Message);
            return await fallback.GetManifestAsync(cancellationToken);
        }
    }
}
