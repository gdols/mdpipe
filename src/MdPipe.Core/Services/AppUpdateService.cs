using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Decides whether this copy of MdPipe is behind the released one.
/// </summary>
/// <remarks>
/// It exists because the portable executable had no way to find out. Of the downloads so far, most
/// are on releases old enough to carry bugs that have since been fixed, and nothing ever told those
/// people a fix shipped. Nothing here downloads or replaces anything: the executable is portable and
/// may well be sitting on a memory stick or in a folder it cannot write to, so the most it does is
/// say a newer one exists and where.
/// </remarks>
public sealed class AppUpdateService(VersionGateService versions)
{
    /// <summary>
    /// What to tell the user, or null when there is nothing worth saying.
    /// </summary>
    /// <param name="runningVersion">The version of the executable doing the asking.</param>
    public AppUpdate? CheckFor(AppRelease? release, string? runningVersion)
    {
        if (release is null) return null;
        if (string.IsNullOrWhiteSpace(runningVersion)) return null;

        // Unparseable on either side means "no idea", and a notice nobody can act on sensibly is
        // worse than silence: a build compiled from a branch should not nag about being behind.
        var behind = versions.Compare(runningVersion, release.LatestVersion);
        if (behind is not < 0) return null;

        return new AppUpdate(
            release.LatestVersion,
            release.ReleaseUrl,
            release.Notes,
            Critical: IsCritical(release, runningVersion),
            DownloadUrl: release.DownloadUrl);
    }

    /// <summary>
    /// Whether the running version is old enough that the notice should stop being polite about it.
    /// </summary>
    private bool IsCritical(AppRelease release, string runningVersion) =>
        !string.IsNullOrWhiteSpace(release.CriticalBelow) &&
        versions.Compare(runningVersion, release.CriticalBelow) is < 0;
}
