using MdPipe.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MdPipe.Core.Services;

/// <summary>
/// The single place that decides what happens on startup: read the manifest, see whether the
/// MarkItDown we have installed is still allowed, and only install or upgrade if we actually need to.
/// The CLI and the desktop app both go through here, so the behaviour stays identical.
/// </summary>
public sealed class SetupOrchestrator(
    IManifestProvider manifestProvider,
    IPythonEnvironmentManager environmentManager,
    VersionGateService versionGate,
    ILogger<SetupOrchestrator> logger)
{
    public async Task<SetupResult> RunAsync(
        bool forceReinstall = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Report(progress, "Checking for compatible MarkItDown version...");
        logger.LogInformation("Fetching compatibility manifest...");
        var manifest = await manifestProvider.GetManifestAsync(cancellationToken);
        logger.LogInformation("Manifest loaded. Stable version: {Version} (updated {Date})", manifest.StableVersion, manifest.UpdatedAt);

        var envInfo = await environmentManager.GetEnvironmentInfoAsync(cancellationToken);

        if (!forceReinstall && envInfo.IsReady && envInfo.InstalledMarkItDownVersion is not null)
        {
            if (versionGate.IsCompatible(envInfo.InstalledMarkItDownVersion, manifest))
            {
                logger.LogInformation("MarkItDown {Version} is already installed and compatible. Nothing to do.", envInfo.InstalledMarkItDownVersion);
                Report(progress, $"MarkItDown {envInfo.InstalledMarkItDownVersion} is ready.");
                return SetupResult.AlreadyUpToDate(envInfo.InstalledMarkItDownVersion);
            }

            logger.LogWarning(
                "Installed version {Installed} is not in the validated set. Upgrading to {Target}.",
                envInfo.InstalledMarkItDownVersion, manifest.StableVersion);
            Report(progress, $"Updating MarkItDown to {manifest.StableVersion}...");
        }

        var targetVersion = versionGate.GetTargetVersion(manifest);
        Report(progress, $"Installing MarkItDown {targetVersion} (this may take a minute the first time)...");
        await environmentManager.SetupAsync(targetVersion, forceReinstall, cancellationToken);
        Report(progress, $"MarkItDown {targetVersion} installed.");

        return SetupResult.Installed(targetVersion);
    }

    private static void Report(IProgress<string>? progress, string message) => progress?.Report(message);
}

public sealed class SetupResult
{
    public bool WasInstalled { get; private init; }
    public string Version { get; private init; } = string.Empty;

    public static SetupResult Installed(string version) => new() { WasInstalled = true, Version = version };
    public static SetupResult AlreadyUpToDate(string version) => new() { WasInstalled = false, Version = version };
}
