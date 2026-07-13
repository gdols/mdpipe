using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Manifest;

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
