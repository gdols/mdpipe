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
/// The engine version comes from the build, through <see cref="IBuildManifestProvider"/>. A release
/// of MdPipe is one application and one MarkItDown that were tried together, and that pairing is
/// decided when the release is cut, not afterwards by whatever the repository happens to say today.
/// New engine features reach people the same way everything else does, in a new release.
/// <para>
/// The remote manifest is still fetched, but only to find out whether a newer MdPipe exists. It
/// never changes what gets installed, so a machine with no route to GitHub still ends up with
/// exactly the right environment.
/// </para>
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

        Report(progress, "Checking the conversion engine...");
        var envInfo = await environmentManager.GetEnvironmentInfoAsync(cancellationToken);
        var installed = envInfo.IsReady ? envInfo.InstalledMarkItDownVersion : null;

        if (!forceReinstall && installed is not null && IsTheVersionThisBuildWants(installed, target))
        {
            logger.LogInformation("MarkItDown {Version} is already installed. Nothing to do.", installed);
            Report(progress, $"MarkItDown {installed} is ready.");
            await environmentManager.EnsureFormatCatalogAsync(installed, cancellationToken);
            return SetupResult.AlreadyUpToDate(installed, await AppReleaseAsync(cancellationToken));
        }

        if (installed is not null)
        {
            logger.LogInformation(
                "Installed MarkItDown is {Installed}, this release wants {Target}. Replacing it.", installed, target);
            Report(progress, $"Switching MarkItDown to {target}...");
        }
        else
        {
            Report(progress, $"Installing MarkItDown {target} (this may take a minute the first time)...");
        }

        await environmentManager.SetupAsync(target, forceReinstall, progress, cancellationToken);
        Report(progress, $"MarkItDown {target} installed.");
        await environmentManager.EnsureFormatCatalogAsync(target, cancellationToken);

        return SetupResult.Installed(target, await AppReleaseAsync(cancellationToken));
    }

    /// <summary>
    /// Whether what is installed is the version this build pins.
    /// </summary>
    /// <remarks>
    /// Compared as versions first, so a release recorded as "0.1.7" and reported by the engine as
    /// "0.1.7.0" counts as a match. Getting that wrong would not be a cosmetic bug: a target that can
    /// never equal what gets installed would reinstall several hundred megabytes on every launch,
    /// forever. Exact text is the fallback for anything the version parser cannot read, which then
    /// gets replaced, since an engine nobody can identify is not the one this release was tried with.
    /// </remarks>
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
    /// <remarks>
    /// Callable on its own because a setup that failed is when this matters most, and is exactly
    /// when it used to be unreachable: the answer only ever came back attached to a run that
    /// finished, so the people a newer release might rescue were the only ones never told about it.
    /// </remarks>
    public Task<AppRelease?> LatestReleaseAsync(CancellationToken cancellationToken = default) =>
        AppReleaseAsync(cancellationToken);

    private async Task<AppRelease?> AppReleaseAsync(CancellationToken cancellationToken)
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

    private static void Report(IProgress<string>? progress, string message) => progress?.Report(message);
}

public sealed class SetupResult
{
    public bool WasInstalled { get; private init; }
    public string Version { get; private init; } = string.Empty;

    /// <summary>
    /// What the repository says the newest MdPipe is, or null when it could not be reached. Handed
    /// back rather than acted on here, because what to do with it depends on who is asking.
    /// </summary>
    public AppRelease? App { get; private init; }

    public static SetupResult Installed(string version, AppRelease? app) =>
        new() { WasInstalled = true, Version = version, App = app };

    public static SetupResult AlreadyUpToDate(string version, AppRelease? app) =>
        new() { WasInstalled = false, Version = version, App = app };
}
