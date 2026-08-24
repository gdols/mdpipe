using System.Text.Json;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Models;

namespace MdPipe.Infrastructure.Manifest;

internal static class ManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CompatibilityManifest Deserialize(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<ManifestDto>(json, Options)
                ?? throw new ManifestException("Manifest deserialized to null.");
            return dto.ToModel();
        }
        catch (JsonException ex)
        {
            // Wrapped here, in the one place that parses, rather than at each call site. The embedded
            // baseline is the bottom of the fallback chain, and a raw JsonException from it would sail
            // past FallbackManifestProvider the way the HttpClient timeout used to.
            throw new ManifestException($"Manifest JSON is malformed: {ex.Message}", ex);
        }
    }

    private sealed class ManifestDto
    {
        public int SchemaVersion { get; set; }
        public string StableVersion { get; set; } = string.Empty;
        public string MinimumVersion { get; set; } = string.Empty;
        public List<string> CompatibleVersions { get; set; } = [];
        public string UpdatedAt { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public AppDto? App { get; set; }

        public CompatibilityManifest ToModel() => new()
        {
            SchemaVersion = SchemaVersion,
            StableVersion = StableVersion,
            MinimumVersion = MinimumVersion,
            CompatibleVersions = CompatibleVersions.AsReadOnly(),
            UpdatedAt = DateOnly.TryParse(UpdatedAt, out var d) ? d : DateOnly.MinValue,
            Notes = Notes,
            App = App?.ToModel()
        };
    }

    /// <summary>
    /// The MdPipe release block. A manifest missing it, or carrying a half-written one, has to leave
    /// the rest of the file usable: the version gate matters more than an update notice.
    /// </summary>
    private sealed class AppDto
    {
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string CriticalBelow { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;

        public AppRelease? ToModel() =>
            string.IsNullOrWhiteSpace(LatestVersion) || string.IsNullOrWhiteSpace(ReleaseUrl)
                ? null
                : new AppRelease(
                    LatestVersion.Trim(), ReleaseUrl.Trim(), CriticalBelow.Trim(), Notes.Trim(), DownloadUrl.Trim());
    }
}
