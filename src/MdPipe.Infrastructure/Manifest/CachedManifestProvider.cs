using System.Text.Json;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Manifest;

/// <summary>
/// Decorator that caches the manifest to disk for <see cref="CacheTtl"/> so the app
/// works offline and doesn't hit GitHub on every run.
/// </summary>
public sealed class CachedManifestProvider : IManifestProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public static string DefaultCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mdpipe", "manifest-cache.json");

    private readonly IManifestProvider _inner;
    private readonly ILogger<CachedManifestProvider> _logger;
    private readonly string _cachePath;

    public CachedManifestProvider(
        IManifestProvider inner,
        ILogger<CachedManifestProvider> logger,
        string? cachePath = null)
    {
        _inner = inner;
        _logger = logger;
        _cachePath = cachePath ?? DefaultCachePath;
    }

    public async Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        if (TryReadCache(out var cached))
        {
            _logger.LogDebug("Using cached manifest (expires in {Remaining})", GetCacheExpiry());
            return cached!;
        }

        _logger.LogDebug("Cache miss or expired — fetching manifest from remote");
        var manifest = await _inner.GetManifestAsync(cancellationToken);
        WriteCache(manifest);
        return manifest;
    }

    private bool TryReadCache(out CompatibilityManifest? manifest)
    {
        manifest = null;
        if (!File.Exists(_cachePath)) return false;

        var lastWrite = File.GetLastWriteTimeUtc(_cachePath);
        if (DateTime.UtcNow - lastWrite > CacheTtl) return false;

        try
        {
            var json = File.ReadAllText(_cachePath);
            manifest = JsonSerializer.Deserialize<CompatibilityManifest>(json);
            return manifest is not null;
        }
        catch
        {
            return false;
        }
    }

    private void WriteCache(CompatibilityManifest manifest)
    {
        var dir = Path.GetDirectoryName(_cachePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_cachePath, JsonSerializer.Serialize(manifest));
    }

    private string GetCacheExpiry()
    {
        if (!File.Exists(_cachePath)) return "unknown";
        var expiry = File.GetLastWriteTimeUtc(_cachePath).Add(CacheTtl) - DateTime.UtcNow;
        return expiry > TimeSpan.Zero ? expiry.ToString(@"h\h\ m\m") : "expired";
    }
}
