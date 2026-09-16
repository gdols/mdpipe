namespace MdPipe.Core.Models;

/// <summary>What the MarkItDown on this machine can read, as reported by the engine itself.</summary>
/// <param name="IsBaseline">The list MdPipe ships with rather than one read from the engine, which is
/// what you get before the first setup finishes.</param>
public sealed record FormatCatalog(
    string EngineVersion,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<FormatConverter> Converters,
    bool IsBaseline = false);

public sealed record FormatConverter(string Name, IReadOnlyList<string> Extensions);
