using System.Text.Json;
using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Answers "what can MdPipe read?" from the engine actually installed on this machine.
/// </summary>
/// <remarks>
/// Setup asks MarkItDown what its converters accept and writes the answer next to the environment;
/// this reads that file. Keeping a hand-written copy of another project's capabilities was the old
/// approach, and it had already drifted: three formats were missing and two that no longer had a
/// converter were still listed.
/// <para>
/// Same shape as the compatibility manifest: a cached answer with a baseline underneath, so a fresh
/// install works before the first setup has finished and a deleted cache is never fatal.
/// </para>
/// </remarks>
public sealed class FormatCatalogProvider
{
    /// <summary>
    /// What MdPipe ships knowing, taken from MarkItDown 0.1.7. Only used until setup writes the real
    /// answer, and flagged as a baseline so the UI can say so.
    /// </summary>
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

    /// <param name="catalogPath">Where setup left the answer. Overridable so tests stay off the real machine.</param>
    public FormatCatalogProvider(string? catalogPath = null) => _path = catalogPath ?? DefaultPath;

    /// <summary>
    /// The current catalog, re-read whenever the file on disk changes so a finished setup takes effect
    /// without restarting the app.
    /// </summary>
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
                // Never fatal: not knowing the exact list is far better than refusing to convert.
                return _cached = Baseline;
            }
        }
    }
}
