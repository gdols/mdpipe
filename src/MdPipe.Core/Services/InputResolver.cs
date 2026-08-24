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
/// <para>
/// Scanning a folder can take a while (a network share, a OneDrive folder full of files that aren't
/// downloaded yet), so the walk reports what it has found and stops when asked. Callers are expected
/// to run it off whichever thread must stay responsive.
/// </para>
/// </remarks>
public sealed class InputResolver(FormatCatalogProvider formats)
{
    /// <summary>
    /// The one format decision MdPipe makes for itself. MarkItDown will happily convert Markdown, but
    /// the output lands exactly where the input was, so a folder scan would rewrite the user's own
    /// notes with a reformatted copy of themselves. Naming a <c>.md</c> file explicitly still works.
    /// </summary>
    private const string SelfOverwritingExtension = ".md";

    /// <summary>How many new matches to collect before telling the caller, so a big scan doesn't
    /// flood the UI thread with one notification per file.</summary>
    private const int ProgressBatch = 200;

    /// <param name="progress">Receives the running count of matching files, in batches.</param>
    /// <param name="cancellationToken">Stops the walk. Whatever was found so far is still returned,
    /// with <see cref="InputResolution.Cancelled"/> set, so cancelling costs the wait and not the work.</param>
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

            // Nothing matched: a typo, an empty folder, a pattern that hit nothing. Worth saying out
            // loud, because silence looks exactly like "converted everything, all good". A scan the
            // user cut short is a different thing, though, and shouldn't be reported as a bad input.
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
    /// The set of extensions a bulk selector will pick up, or null when the caller wants everything and
    /// is happy to let MarkItDown decide by content (which is how a file with a wrong or missing
    /// extension gets converted at all).
    /// </summary>
    private HashSet<string>? BuildFilter(bool includeEverything) =>
        includeEverything ? null : new HashSet<string>(formats.Get().Extensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>State shared by every walk in one call: what to keep, what couldn't be opened, and how
    /// far along we are.</summary>
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
    /// The manual walk is the point. <c>Directory.EnumerateFiles</c> with <c>AllDirectories</c> is lazy,
    /// so a locked subfolder throws later, while the caller is iterating, where no try/catch of ours can
    /// help. Going directory by directory with the eager <c>GetFiles</c>/<c>GetDirectories</c> keeps each
    /// failure contained, and every folder we couldn't open is recorded so the caller can report it.
    /// Filtering inside the walk rather than after it means a folder with a million files never builds a
    /// million-entry list just to throw most of it away.
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

        // Sorted per expansion rather than across the whole call: a folder should come out in a
        // predictable order, but files the user named by hand keep the order they typed them in.
        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    /// <summary>
    /// Expands patterns like <c>*.pdf</c> or <c>docs\report?.docx</c>. Windows shells hand wildcards
    /// through untouched, so the expansion has to happen here.
    /// </summary>
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
