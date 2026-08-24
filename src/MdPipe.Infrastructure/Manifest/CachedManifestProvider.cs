using System.Text.Json;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Manifest;

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

        _logger.LogDebug("Cache miss or expired. Fetching manifest from remote");
        try
        {
            var manifest = await _inner.GetManifestAsync(cancellationToken);
            WriteCache(manifest);
            return manifest;
        }
        catch (ManifestException ex)
        {
            // An expired cache used to be thrown away outright, which sent an offline machine all the
            // way down to the copy baked into the build. Yesterday's answer is a much better guess
            // than one from whenever this version was released.
            if (!TryReadCache(out var stale, ignoreAge: true)) throw;

            _logger.LogWarning(
                "Remote manifest unavailable ({Reason}). Falling back to the cached copy from {Written:u}.",
                ex.Message, File.GetLastWriteTimeUtc(_cachePath));
            return stale!;
        }
    }

    private bool TryReadCache(out CompatibilityManifest? manifest, bool ignoreAge = false)
    {
        manifest = null;
        if (!File.Exists(_cachePath)) return false;

        var lastWrite = File.GetLastWriteTimeUtc(_cachePath);
        if (!ignoreAge && DateTime.UtcNow - lastWrite > CacheTtl) return false;

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
