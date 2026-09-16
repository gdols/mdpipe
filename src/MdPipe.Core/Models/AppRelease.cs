namespace MdPipe.Core.Models;

/// <summary>
/// What the manifest says about MdPipe itself, as opposed to the engine it wraps. It rides in the
/// compatibility manifest because that file is already fetched and cached on every launch.
/// </summary>
/// <param name="CriticalBelow">Versions older than this have a known problem. Empty when none do.</param>
/// <param name="DownloadUrl">The versioned asset, not a "latest" URL. Empty means send them to the
/// release page instead.</param>
public sealed record AppRelease(
    string LatestVersion,
    string ReleaseUrl,
    string CriticalBelow = "",
    string Notes = "",
    string DownloadUrl = "");
