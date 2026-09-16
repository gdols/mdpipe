using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Turns whatever the user pointed at (files, folders, wildcards) into the list of documents to
/// convert, so both front-ends decide "what counts as convertible" the same way.
/// </summary>
/// <remarks>
/// A file named explicitly is always taken, whatever its extension. Folders and wildcards are bulk
/// selectors, so those are filtered down to what the installed MarkItDown says it can read. The walk
/// reports progress and stops when asked, and callers run it off whichever thread must stay alive.
/// </remarks>
public sealed class InputResolver(FormatCatalogProvider formats)
{
    /// <summary>
    /// MarkItDown will happily convert Markdown, but the output lands where the input was, so a
    /// folder scan would rewrite the user's own notes. Naming a .md file explicitly still works.
    /// </summary>
    private const string SelfOverwritingExtension = ".md";

    /// <summary>Matches to collect between progress reports, so a big scan doesn't flood the UI.</summary>
    private const int ProgressBatch = 200;

    /// <param name="progress">Receives the running count of matching files, in batches.</param>
    /// <param name="cancellationToken">Stops the walk. What was found so far is still returned, so
    /// cancelling costs the wait and not the work.</param>
    public InputResolution Resolve(
        IEnumerable<string> inputs,
        bool recursive = false,
        bool includeEverything = false,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var scan = new Scan(BuildFilter(includeEverything), progress, cancellationToken);

        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notFound = new List<string>();

        foreach (var raw in inputs)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var input = raw.Trim().Trim('"');

            // An explicit file is taken as-is; folders and patterns go through the filter.
            var matches = File.Exists(input)
                ? [input]
                : Directory.Exists(input)
                    ? WalkFiles(input, "*", recursive, scan)
                    : input.Contains('*') || input.Contains('?')
                        ? ExpandWildcard(input, recursive, scan)
                        : (IReadOnlyList<string>)[];

            // A typo, an empty folder, a pattern that hit nothing. Worth saying, because silence
            // looks exactly like "converted everything". A scan cut short is not a bad input.
            if (matches.Count == 0)
            {
                if (!cancellationToken.IsCancellationRequested) notFound.Add(input);
                continue;
            }

            foreach (var path in matches)
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full)) files.Add(full);
            }
        }

        scan.ReportNow(files.Count);

        return new InputResolution(files, notFound, scan.Unreadable, cancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// The extensions a bulk selector picks up, or null to let MarkItDown decide by content, which is
    /// how a file with a wrong or missing extension gets converted at all.
    /// </summary>
    private HashSet<string>? BuildFilter(bool includeEverything) =>
        includeEverything ? null : new HashSet<string>(formats.Get().Extensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>State shared by every walk in one call.</summary>
    private sealed class Scan(HashSet<string>? supported, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        private int _found;
        private int _reported;

        public List<string> Unreadable { get; } = [];
        public CancellationToken CancellationToken => cancellationToken;

        public bool Accepts(string path)
        {
            var extension = Path.GetExtension(path);
            if (extension.Equals(SelfOverwritingExtension, StringComparison.OrdinalIgnoreCase)) return false;
            return supported is null || supported.Contains(extension);
        }

        public void Count(int matches)
        {
            _found += matches;
            if (_found - _reported >= ProgressBatch) ReportNow(_found);
        }

        public void ReportNow(int total)
        {
            _reported = total;
            progress?.Report(total);
        }
    }

    /// <summary>
    /// Walks a tree one directory at a time, keeping the files that pass the filter.
    /// </summary>
    /// <remarks>
    /// The manual walk is the point: EnumerateFiles with AllDirectories is lazy, so a locked subfolder
    /// throws later, while the caller is iterating, where no try/catch here can help. Eager GetFiles
    /// per directory keeps each failure contained and records the folders we couldn't open.
    /// </remarks>
    private static List<string> WalkFiles(string root, string mask, bool recursive, Scan scan)
    {
        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            if (scan.CancellationToken.IsCancellationRequested) break;

            var dir = pending.Pop();
            try
            {
                if (recursive)
                    foreach (var sub in Directory.GetDirectories(dir))
                        pending.Push(sub);

                var before = results.Count;
                foreach (var file in Directory.GetFiles(dir, mask))
                    if (scan.Accepts(file))
                        results.Add(file);

                scan.Count(results.Count - before);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                scan.Unreadable.Add(dir);
            }
        }

        // Per expansion, not across the whole call: a folder comes out in a predictable order, but
        // files named by hand keep the order they were typed in.
        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    /// <summary>Expands patterns like *.pdf. Windows shells hand wildcards through untouched.</summary>
    private static List<string> ExpandWildcard(string pattern, bool recursive, Scan scan)
    {
        var directory = Path.GetDirectoryName(pattern);
        var mask = Path.GetFileName(pattern);

        if (string.IsNullOrEmpty(mask)) return [];
        if (string.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();
        if (!Directory.Exists(directory)) return [];

        return WalkFiles(directory, mask, recursive, scan);
    }
}
