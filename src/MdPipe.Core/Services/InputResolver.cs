using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Turns whatever the user pointed at (files, folders, wildcards) into the actual list of documents to
/// convert. Both front-ends go through here so "what counts as convertible" is decided in one place.
/// </summary>
/// <remarks>
/// A file named explicitly is always taken, whatever its extension: if you asked for it, you meant it.
/// Folders and wildcards are bulk selectors, so those are filtered down to the formats we support.
/// </remarks>
public sealed class InputResolver
{
    /// <summary>
    /// What MarkItDown actually converts, taken from the ACCEPTED_FILE_EXTENSIONS of its converter
    /// modules in 0.1.7 rather than from its README, which is looser than the code. Updating MarkItDown
    /// is the moment to revisit this list.
    /// </summary>
    /// <remarks>
    /// Two deliberate departures from that list:
    /// <list type="bullet">
    /// <item><c>.md</c> is left out. MarkItDown accepts it, but converting Markdown to Markdown lands the
    /// output on top of the input, so a folder scan would quietly rewrite the user's own notes.</item>
    /// <item><c>.xml</c> is kept even though no converter claims it. MarkItDown sniffs the content and
    /// converts it as text, verified against a real file.</item>
    /// </list>
    /// <c>.doc</c> and <c>.ppt</c> used to be here and are gone: the legacy binary formats have no
    /// converter at all, so every one of them failed at conversion time and polluted the batch summary.
    /// </remarks>
    public static readonly IReadOnlyCollection<string> SupportedExtensions =
    [
        ".pdf", ".epub", ".zip", ".msg",
        ".docx", ".pptx", ".xlsx", ".xls",
        ".html", ".htm", ".csv", ".json", ".jsonl", ".xml", ".ipynb",
        ".txt", ".text", ".markdown",
        ".png", ".jpg", ".jpeg",
        ".mp3", ".mp4", ".m4a", ".wav"
    ];

    private static readonly HashSet<string> SupportedSet = new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    public InputResolution Resolve(IEnumerable<string> inputs, bool recursive = false)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notFound = new List<string>();
        var unreadable = new List<string>();

        foreach (var raw in inputs)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var input = raw.Trim().Trim('"');

            // An explicit file is taken as-is; folders and patterns are filtered to what we can convert.
            var matches = File.Exists(input)
                ? [input]
                : Directory.Exists(input)
                    ? Supported(WalkFiles(input, "*", recursive, unreadable))
                    : input.Contains('*') || input.Contains('?')
                        ? Supported(ExpandWildcard(input, recursive, unreadable))
                        : (IReadOnlyList<string>)[];

            // Nothing matched: a typo, an empty folder, a pattern that hit nothing. Worth saying out
            // loud, because silence looks exactly like "converted everything, all good".
            if (matches.Count == 0)
            {
                notFound.Add(input);
                continue;
            }

            foreach (var path in matches)
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full)) files.Add(full);
            }
        }

        return new InputResolution(files, notFound, unreadable);
    }

    private static IReadOnlyList<string> Supported(IEnumerable<string> candidates) =>
        candidates
            .Where(p => SupportedSet.Contains(Path.GetExtension(p)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Walks a tree one directory at a time, matching <paramref name="mask"/> as it goes.
    /// </summary>
    /// <remarks>
    /// The manual walk is the point. <c>Directory.EnumerateFiles</c> with <c>AllDirectories</c> is lazy,
    /// so a locked subfolder throws later, while the caller is iterating, where no try/catch of ours can
    /// help. Going directory by directory with the eager <c>GetFiles</c>/<c>GetDirectories</c> keeps each
    /// failure contained, and every folder we couldn't open is recorded so the caller can report it.
    /// </remarks>
    private static List<string> WalkFiles(string root, string mask, bool recursive, List<string> unreadable)
    {
        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                if (recursive)
                    foreach (var sub in Directory.GetDirectories(dir))
                        pending.Push(sub);

                results.AddRange(Directory.GetFiles(dir, mask));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                unreadable.Add(dir);
            }
        }

        return results;
    }

    /// <summary>
    /// Expands patterns like <c>*.pdf</c> or <c>docs\report?.docx</c>. Windows shells hand wildcards
    /// through untouched, so the expansion has to happen here.
    /// </summary>
    private static List<string> ExpandWildcard(string pattern, bool recursive, List<string> unreadable)
    {
        var directory = Path.GetDirectoryName(pattern);
        var mask = Path.GetFileName(pattern);

        if (string.IsNullOrEmpty(mask)) return [];
        if (string.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();
        if (!Directory.Exists(directory)) return [];

        return WalkFiles(directory, mask, recursive, unreadable);
    }
}
