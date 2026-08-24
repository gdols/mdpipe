using MdPipe.Core.Models;

namespace MdPipe.Core.Interfaces;

/// <summary>
/// Replaces the running MdPipe with a newer one, when the user asks for it.
/// </summary>
/// <remarks>
/// Nothing here happens on its own. The application finds out that a newer release exists, says
/// so, and this runs only if the person says yes.
/// <para>
/// It refuses more readily than it acts. The executable is portable, so it can be sitting on a
/// memory stick, in a folder nobody has write permission on, or somewhere an administrator would
/// have to approve. In any of those cases <see cref="CanInstall"/> is false and the caller should
/// send the user to the release page instead of trying and failing halfway through.
/// </para>
/// </remarks>
public interface IAppUpdateInstaller
{
    /// <summary>
    /// Whether replacing this executable in place is possible at all, checked before anything is
    /// downloaded so a doomed attempt never starts.
    /// </summary>
    bool CanInstall { get; }

    /// <summary>
    /// Downloads the new executable, checks it against the hash published beside it, and puts it
    /// where the current one is. The caller then starts the returned path and exits.
    /// </summary>
    /// <returns>The path to launch. The old executable is left alongside, locked until this
    /// process ends, and swept up by <see cref="CleanUpPreviousUpdate"/> next time.</returns>
    /// <exception cref="Exceptions.AppUpdateException">
    /// The download failed, the hash did not match, or the swap could not be completed. Nothing is
    /// left half-done: the running executable is only moved once a verified replacement is ready.
    /// </exception>
    Task<string> InstallAsync(
        AppUpdate update, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes what a previous update left behind. Windows will not let a running executable be
    /// deleted, only renamed, so the last version is still on disk until the next launch.
    /// </summary>
    void CleanUpPreviousUpdate();
}
