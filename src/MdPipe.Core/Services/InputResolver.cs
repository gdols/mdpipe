using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Turns whatever the user pointed at (files, folders, wildcards) into the actual list of documents to
/// convert. Both front-ends go through here so "what counts as convertible" is decided in one place.
/// </summary>
/// <remarks>
/// A file named explicitly is always taken, whatever its extension: if you asked for it, you meant it.
/// Folders and wildcards are bulk selectors, so those are filtered down to what the installed
/// MarkItDown says it can read, unless the caller asks for everything.
/// </remarks>
public sealed class InputResolver(FormatCatalogProvider formats)
{
    /// <summary>
    /// The one format decision MdPipe makes for itself. MarkItDown will happily convert Markdown, but
    /// the output lands exactly where the input was, so a folder scan would rewrite the user's own
    /// notes with a reformatted copy of themselves. Naming a <c>.md</c> file explicitly still works.
    /// </summary>
    private const string SelfOverwritingExtension = ".md";

    public InputResolution Resolve(
        IEnumerable<string> inputs, bool recursive = false, bool includeEverything = false)
    {
        var supported = BuildFilter(includeEverything);

        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notFound = new List<string>();
        var unreadable = new List<string>();

        foreach (var raw in inputs)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var input = raw.Trim().Trim('"');

            // An explicit file is taken as-is; folders and patterns go through the filter.
            var matches = File.Exists(input)
                ? [input]
                : Directory.Exists(input)
                    ? Filter(WalkFiles(input, "*", recursive, unreadable), supported)
                    : input.Contains('*') || input.Contains('?')
                        ? Filter(ExpandWildcard(input, recursive, unreadable), supported)
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

    /// <summary>
    /// The set of extensions a bulk selector will pick up, or null when the caller wants everything and
    /// is happy to let MarkItDown decide by content (which is how a file with a wrong or missing
    /// extension gets converted at all).
    /// </summary>
    private HashSet<string>? BuildFilter(bool includeEverything) =>
        includeEverything ? null : new HashSet<string>(formats.Get().Extensions, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Filter(IEnumerable<string> candidates, HashSet<string>? supported) =>
        candidates
            .Where(p =>
            {
                var extension = Path.GetExtension(p);
                if (extension.Equals(SelfOverwritingExtension, StringComparison.OrdinalIgnoreCase)) return false;
                return supported is null || supported.Contains(extension);
            })
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
