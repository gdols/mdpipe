using System.Text.Json;
using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Answers "what can MdPipe read?" from the engine actually installed on this machine. Setup asks
/// MarkItDown what its converters accept and writes the answer next to the environment; this reads
/// that file, with a baseline underneath so a fresh install works before setup has finished.
/// </summary>
public sealed class FormatCatalogProvider
{
    /// <summary>What MdPipe ships knowing, from MarkItDown 0.1.7, until setup writes the real answer.</summary>
    private static readonly FormatCatalog Baseline = new(
        EngineVersion: "bundled list",
        Extensions:
        [
            ".atom", ".csv", ".docx", ".epub", ".htm", ".html", ".ipynb", ".jpeg", ".jpg", ".json",
            ".jsonl", ".m4a", ".markdown", ".md", ".mp3", ".mp4", ".msg", ".pdf", ".png", ".pptx",
            ".rss", ".text", ".txt", ".wav", ".xls", ".xlsx", ".xml", ".zip"
        ],
        Converters: [],
        IsBaseline: true);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpipe", "formats.json");

    private readonly string _path;
    private readonly Lock _gate = new();
    private FormatCatalog? _cached;
    private DateTime _cachedStamp;

    /// <param name="catalogPath">Where setup left the answer. Overridable for tests.</param>
    public FormatCatalogProvider(string? catalogPath = null) => _path = catalogPath ?? DefaultPath;

    /// <summary>Re-read whenever the file changes, so a finished setup takes effect without a restart.</summary>
    public FormatCatalog Get()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return _cached = Baseline;

                var stamp = File.GetLastWriteTimeUtc(_path);
                if (_cached is not null && stamp == _cachedStamp) return _cached;

                var catalog = JsonSerializer.Deserialize<FormatCatalog>(File.ReadAllText(_path), JsonOptions);
                if (catalog is null || catalog.Extensions.Count == 0) return _cached = Baseline;

                _cachedStamp = stamp;
                return _cached = catalog;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Never fatal: not knowing the list beats refusing to convert.
                return _cached = Baseline;
            }
        }
    }
}
