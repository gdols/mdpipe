namespace MdPipe.Core.Models;

/// <summary>
/// A newer MdPipe exists and this copy is older than it. Only ever built when there is genuinely
/// something to tell the user, so anything holding one of these should show it.
/// </summary>
/// <param name="Critical">The running version is old enough to have a known problem.</param>
public sealed record AppUpdate(
    string Version,
    string ReleaseUrl,
    string Notes,
    bool Critical);
