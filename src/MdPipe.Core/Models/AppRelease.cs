namespace MdPipe.Core.Models;

/// <summary>
/// What the manifest says about MdPipe itself, as opposed to the engine it wraps.
/// </summary>
/// <remarks>
/// It travels in the compatibility manifest rather than in a call of its own because that file is
/// already fetched on every launch, already cached for a day and already has a baseline underneath
/// it. Asking the GitHub releases API instead would mean a second request, plus its limit of sixty
/// an hour per address, which an office behind one NAT reaches on its own.
/// <para>
/// Older builds ignore the block: the serializer does not reject unknown fields, so every copy of
/// MdPipe already out there keeps reading the manifest exactly as before.
/// </para>
/// </remarks>
/// <param name="CriticalBelow">Versions older than this have a known problem, so the notice says so
/// plainly instead of reading as an optional upgrade. Empty when no release is that bad.</param>
/// <param name="DownloadUrl">The executable for this release, at its versioned address rather than
/// a "latest" one that would start pointing somewhere else. Empty means MdPipe can only send the
/// user to <paramref name="ReleaseUrl"/> and let them fetch it themselves.</param>
public sealed record AppRelease(
    string LatestVersion,
    string ReleaseUrl,
    string CriticalBelow = "",
    string Notes = "",
    string DownloadUrl = "");
