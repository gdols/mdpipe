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
    public static readonly IReadOnlyCollection<string> SupportedExtensions =
    [
        ".pdf", ".docx", ".doc", ".pptx", ".ppt", ".xlsx", ".xls",
        ".html", ".htm", ".csv", ".json", ".xml", ".txt", ".png", ".jpg", ".jpeg"
    ];

    private static readonly HashSet<string> SupportedSet = new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    public InputResolution Resolve(IEnumerable<string> inputs, bool recursive = false)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notFound = new List<string>();

        foreach (var raw in inputs)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var input = raw.Trim().Trim('"');

            // An explicit file is taken as-is; folders and patterns are filtered to what we can convert.
            var matches = File.Exists(input)
                ? [input]
                : Directory.Exists(input)
                    ? Supported(EnumerateFolder(input, recursive))
                    : input.Contains('*') || input.Contains('?')
                        ? Supported(ExpandWildcard(input, recursive))
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

        return new InputResolution(files, notFound);
    }

    private static IReadOnlyList<string> Supported(IEnumerable<string> candidates) =>
        candidates
            .Where(p => SupportedSet.Contains(Path.GetExtension(p)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Walks a folder iteratively so one unreadable subfolder costs us that subfolder,
    /// not the whole batch (Directory.EnumerateFiles with AllDirectories throws and stops).
    /// </summary>
    private static IEnumerable<string> EnumerateFolder(string root, bool recursive)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] files;
            try
            {
                if (recursive)
                    foreach (var sub in Directory.GetDirectories(dir))
                        pending.Push(sub);

                files = Directory.GetFiles(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;
        }
    }

    /// <summary>
    /// Expands patterns like <c>*.pdf</c> or <c>docs\report?.docx</c>. Windows shells hand wildcards
    /// through untouched, so the expansion has to happen here.
    /// </summary>
    private static IEnumerable<string> ExpandWildcard(string pattern, bool recursive)
    {
        var directory = Path.GetDirectoryName(pattern);
        var mask = Path.GetFileName(pattern);

        if (string.IsNullOrEmpty(mask)) return [];
        if (string.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();
        if (!Directory.Exists(directory)) return [];

        try
        {
            return Directory.EnumerateFiles(
                directory, mask,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
