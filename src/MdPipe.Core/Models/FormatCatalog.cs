namespace MdPipe.Core.Models;

/// <summary>
/// What the MarkItDown installed on this machine can read, as reported by the engine itself.
/// </summary>
/// <param name="EngineVersion">The MarkItDown version the list came from.</param>
/// <param name="Extensions">Every extension any converter accepts, lowercase and sorted.</param>
/// <param name="Converters">The same information broken down per converter, for display.</param>
/// <param name="IsBaseline">
/// True when this is the list MdPipe ships with rather than one read from the installed engine,
/// which happens before the first setup finishes. Worth showing, so nobody is told their machine
/// supports something it hasn't installed yet.
/// </param>
public sealed record FormatCatalog(
    string EngineVersion,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<FormatConverter> Converters,
    bool IsBaseline = false);

/// <param name="Name">The converter's class name, e.g. <c>PdfConverter</c>.</param>
public sealed record FormatConverter(string Name, IReadOnlyList<string> Extensions);
