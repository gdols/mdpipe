using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Works out where each converted document goes, making sure one batch never writes two documents to
/// the same file. Create one per run: it remembers the paths it has already handed out.
/// </summary>
/// <remarks>
/// Converting a folder tree into a single output folder flattens it, so <c>2025\report.pdf</c> and
/// <c>2026\report.pdf</c> both want to be <c>report.md</c>. Rather than let the second quietly replace
/// the first, the name is given a suffix and the caller is told, so nothing is lost and the run can
/// say what it did.
/// <para>
/// Only clashes within the same run are avoided. Overwriting the output of an earlier run is what
/// re-converting a folder is supposed to do, and stays untouched.
/// </para>
/// </remarks>
public sealed class OutputPathResolver
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="sourcePath">The document being converted.</param>
    /// <param name="outputFolder">Where to write, or null to sit next to the original.</param>
    public OutputPath For(string sourcePath, string? outputFolder)
    {
        var source = Path.GetFullPath(sourcePath);
        var folder = string.IsNullOrEmpty(outputFolder)
            ? Path.GetDirectoryName(source)!
            : outputFolder;
        var name = Path.GetFileNameWithoutExtension(source);

        var candidate = Path.GetFullPath(Path.Combine(folder, name + ".md"));
        var attempt = 1;

        // Also guards the case where the destination is the source itself, which happens when someone
        // names a Markdown file explicitly. Converting a file onto itself would destroy the original.
        while (_used.Contains(candidate) || candidate.Equals(source, StringComparison.OrdinalIgnoreCase))
        {
            attempt++;
            candidate = Path.GetFullPath(Path.Combine(folder, $"{name}-{attempt}.md"));
        }

        _used.Add(candidate);
        return new OutputPath(candidate, attempt > 1);
    }
}
