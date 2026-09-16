using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Works out where each converted document goes. Create one per run: it remembers what it handed out.
/// </summary>
/// <remarks>
/// Converting a tree into one output folder flattens it, so 2025\report.pdf and 2026\report.pdf both
/// want to be report.md. The second gets a suffix and the caller is told. Only clashes within the
/// same run are avoided: overwriting an earlier run is what re-converting a folder means.
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

        // Also guards the destination being the source itself, which happens when someone names a
        // Markdown file explicitly. Converting a file onto itself would destroy it.
        while (_used.Contains(candidate) || candidate.Equals(source, StringComparison.OrdinalIgnoreCase))
        {
            attempt++;
            candidate = Path.GetFullPath(Path.Combine(folder, $"{name}-{attempt}.md"));
        }

        _used.Add(candidate);
        return new OutputPath(candidate, attempt > 1);
    }
}
