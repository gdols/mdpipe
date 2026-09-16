using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Core.Services;

/// <summary>
/// Gets the machine into the state this release expects: the right MarkItDown installed, and a
/// record of what it can read.
/// </summary>
/// <remarks>
/// The engine version comes from the build, not from the repository. A release of MdPipe is one
/// application and one MarkItDown that were tried together, and that pairing is decided when the
/// release is cut. The remote manifest is still fetched, but only to find out whether a newer
/// MdPipe exists, so a machine with no route to GitHub still gets the right environment.
/// </remarks>
public sealed class SetupOrchestrator(
    IBuildManifestProvider buildManifest,
    IManifestProvider remoteManifest,
    IPythonEnvironmentManager environmentManager,
    VersionGateService versionGate,
    ILogger<SetupOrchestrator> logger)
{
    public async Task<SetupResult> RunAsync(
        bool forceReinstall = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = await buildManifest.GetManifestAsync(cancellationToken);
        var target = versionGate.GetTargetVersion(manifest);
        logger.LogInformation("This build of MdPipe expects MarkItDown {Version}.", target);

        progress?.Report("Checking the conversion engine...");
        var envInfo = await environmentManager.GetEnvironmentInfoAsync(cancellationToken);
        var installed = envInfo.IsReady ? envInfo.InstalledMarkItDownVersion : null;

        if (!forceReinstall && installed is not null && IsTheVersionThisBuildWants(installed, target))
        {
            logger.LogInformation("MarkItDown {Version} is already installed. Nothing to do.", installed);
            progress?.Report($"MarkItDown {installed} is ready.");
            await environmentManager.EnsureFormatCatalogAsync(installed, cancellationToken);
            return SetupResult.AlreadyUpToDate(installed, await LatestReleaseAsync(cancellationToken));
        }

        if (installed is not null)
        {
            logger.LogInformation(
                "Installed MarkItDown is {Installed}, this release wants {Target}. Replacing it.", installed, target);
            progress?.Report($"Switching MarkItDown to {target}...");
        }
        else
        {
            progress?.Report($"Installing MarkItDown {target} (this may take a minute the first time)...");
        }

        await environmentManager.SetupAsync(target, forceReinstall, progress, cancellationToken);
        progress?.Report($"MarkItDown {target} installed.");
        await environmentManager.EnsureFormatCatalogAsync(target, cancellationToken);

        return SetupResult.Installed(target, await LatestReleaseAsync(cancellationToken));
    }

    /// <summary>
    /// Whether what is installed is the version this build pins. Compared as versions, not as text,
    /// so "0.1.7" and "0.1.7.0" count as a match. Getting that wrong would reinstall several hundred
    /// megabytes on every single launch. Exact text is the fallback for anything the parser cannot
    /// read, which then gets replaced.
    /// </summary>
    private bool IsTheVersionThisBuildWants(string installed, string target) =>
        versionGate.Compare(installed, target) switch
        {
            0 => true,
            null => string.Equals(installed, target, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    /// <summary>
    /// Asks the repository whether a newer MdPipe has been released. Advisory only, so anything that
    /// goes wrong here costs the notice and nothing else.
    /// </summary>
    public async Task<AppRelease?> LatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return (await remoteManifest.GetManifestAsync(cancellationToken)).App;
        }
        catch (Exception ex) when (ex is MdPipeException or OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not check for a newer MdPipe.");
            return null;
        }
    }
}

/// <param name="App">What the repository says the newest MdPipe is, or null when it couldn't be
/// reached. Handed back rather than acted on here: what to do with it depends on who is asking.</param>
public sealed record SetupResult(bool WasInstalled, string Version, AppRelease? App)
{
    public static SetupResult Installed(string version, AppRelease? app) => new(true, version, app);

    public static SetupResult AlreadyUpToDate(string version, AppRelease? app) => new(false, version, app);
}
