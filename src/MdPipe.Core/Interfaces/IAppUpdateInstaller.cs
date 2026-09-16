using MdPipe.Core.Models;

namespace MdPipe.Core.Interfaces;

/// <summary>
/// Replaces the running MdPipe with a newer one, after the user has said yes. Nothing here happens
/// on its own.
/// </summary>
public interface IAppUpdateInstaller
{
    /// <summary>
    /// Whether replacing this executable in place is possible at all. False on read-only media, in
    /// Program Files, and anywhere else a portable exe can legitimately end up. Checked before
    /// anything is downloaded, so a doomed attempt never starts.
    /// </summary>
    bool CanInstall { get; }

    /// <summary>
    /// Downloads the new executable, checks it against the published hash, and puts it where the
    /// current one is.
    /// </summary>
    /// <returns>The path to launch. The old executable is left alongside, locked until this process
    /// ends, and removed by <see cref="CleanUpPreviousUpdate"/> next time.</returns>
    /// <exception cref="Exceptions.AppUpdateException">
    /// Nothing is left half-done: the running executable only moves once a verified replacement is
    /// ready.</exception>
    Task<string> InstallAsync(
        AppUpdate update, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes what a previous update left behind. Windows will not delete a running executable,
    /// only rename it, so the old one sits there until the next launch.
    /// </summary>
    void CleanUpPreviousUpdate();
}
