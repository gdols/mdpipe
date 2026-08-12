namespace MdPipe.Core.Models;

/// <summary>
/// The outcome of expanding what the user asked for: the documents to convert, plus the inputs that
/// matched nothing, so a typo or an empty folder can be reported instead of silently ignored.
/// </summary>
public sealed record InputResolution(IReadOnlyList<string> Files, IReadOnlyList<string> NotFound);
