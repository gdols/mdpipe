using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Works out where each converted document of a batch goes, so one batch never writes two documents
/// to the same file.
/// </summary>
/// <remarks>
/// Two sources that would both become report.md are told apart by their extension when they have
/// different ones (report.pdf.md, report.docx.md), because report-2.md says nothing about which is
/// which. Same name and same extension, as when a tree is flattened into one output folder, still
/// gets a number. Files already on disk are only avoided when the caller asks: overwriting an
/// earlier run is what re-converting a folder normally means.
/// </remarks>
public static class OutputPathResolver
{
    /// <param name="sources">The documents being converted, in order.</param>
    /// <param name="outputFolder">Where to write, or null to sit next to each original.</param>
    /// <param name="keepExisting">Leave files that already exist alone and pick another name.</param>
    public static IReadOnlyList<OutputPath> Plan(
        IReadOnlyList<string> sources, string? outputFolder, bool keepExisting = false)
    {
        var full = sources.Select(Path.GetFullPath).ToList();

        string FolderOf(string source) =>
            string.IsNullOrEmpty(outputFolder) ? Path.GetDirectoryName(source)! : outputFolder;

        string Plain(string source) =>
            Path.GetFullPath(Path.Combine(FolderOf(source), Path.GetFileNameWithoutExtension(source) + ".md"));

        var byExtension = full
            .GroupBy(Plain, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(Path.GetExtension).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .SelectMany(g => g)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<OutputPath>(full.Count);

        foreach (var source in full)
        {
            var folder = FolderOf(source);
            var stem = byExtension.Contains(source)
                ? Path.GetFileName(source)
                : Path.GetFileNameWithoutExtension(source);

            var candidate = Path.GetFullPath(Path.Combine(folder, stem + ".md"));
            var attempt = 1;

            // Also guards the destination being the source itself, which happens when someone names a
            // Markdown file explicitly. Converting a file onto itself would destroy it.
            while (used.Contains(candidate) ||
                   candidate.Equals(source, StringComparison.OrdinalIgnoreCase) ||
                   (keepExisting && File.Exists(candidate)))
            {
                attempt++;
                candidate = Path.GetFullPath(Path.Combine(folder, $"{stem}-{attempt}.md"));
            }

            used.Add(candidate);
            plan.Add(new OutputPath(candidate, !candidate.Equals(Plain(source), StringComparison.OrdinalIgnoreCase)));
        }

        return plan;
    }
}
