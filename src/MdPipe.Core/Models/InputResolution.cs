namespace MdPipe.Core.Models;

/// <summary>
/// The outcome of expanding what the user asked for: the documents to convert, the inputs that matched
/// nothing, and the folders we weren't allowed to open. The last two exist so a partial run can never be
/// mistaken for a complete one.
/// </summary>
/// <param name="Cancelled">The scan was stopped early, so <paramref name="Files"/> is whatever had been
/// found by then. The desktop app keeps that partial list, since the user cancelled the waiting and not
/// the work; the CLI treats it as an abort.</param>
public sealed record InputResolution(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> NotFound,
    IReadOnlyList<string> Unreadable,
    bool Cancelled = false);
