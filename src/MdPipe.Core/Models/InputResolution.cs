namespace MdPipe.Core.Models;

/// <summary>
/// What expanding the user's input produced. <paramref name="NotFound"/> and
/// <paramref name="Unreadable"/> are there so a partial run can't look like a complete one.
/// </summary>
/// <param name="Cancelled">Stopped early, so <paramref name="Files"/> is whatever had been found by
/// then. The window keeps that partial list; the CLI treats it as an abort.</param>
public sealed record InputResolution(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> NotFound,
    IReadOnlyList<string> Unreadable,
    bool Cancelled = false);
