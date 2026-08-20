namespace MdPipe.Core.Models;

/// <summary>
/// The outcome of expanding what the user asked for: the documents to convert, the inputs that matched
/// nothing, and the folders we weren't allowed to open. The last two exist so a partial run can never be
/// mistaken for a complete one.
/// </summary>
public sealed record InputResolution(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> NotFound,
    IReadOnlyList<string> Unreadable);
