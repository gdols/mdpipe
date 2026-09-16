using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// Decides whether this copy of MdPipe is behind the released one. Says so and nothing more: the
/// executable is portable and may not even be able to write to its own folder.
/// </summary>
public sealed class AppUpdateService(VersionGateService versions)
{
    /// <param name="runningVersion">The version of the executable doing the asking.</param>
    /// <returns>What to tell the user, or null when there is nothing worth saying.</returns>
    public AppUpdate? CheckFor(AppRelease? release, string? runningVersion)
    {
        if (release is null) return null;
        if (string.IsNullOrWhiteSpace(runningVersion)) return null;

        // Unparseable on either side means "no idea". A build compiled from a branch should not nag.
        var behind = versions.Compare(runningVersion, release.LatestVersion);
        if (behind is not < 0) return null;

        return new AppUpdate(
            release.LatestVersion,
            release.ReleaseUrl,
            release.Notes,
            Critical: IsCritical(release, runningVersion),
            DownloadUrl: release.DownloadUrl);
    }

    private bool IsCritical(AppRelease release, string runningVersion) =>
        !string.IsNullOrWhiteSpace(release.CriticalBelow) &&
        versions.Compare(runningVersion, release.CriticalBelow) is < 0;
}
