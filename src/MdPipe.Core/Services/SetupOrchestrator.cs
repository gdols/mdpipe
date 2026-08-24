using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Core.Services;

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
            var comparison = versionGate.Compare(manifest.StableVersion, envInfo.InstalledMarkItDownVersion);
            if (comparison is null)
                logger.LogWarning(
                    "Couldn't compare installed MarkItDown {Installed} with stable {Stable}; keeping the installed one.",
                    envInfo.InstalledMarkItDownVersion, manifest.StableVersion);

            if (versionGate.IsCompatible(envInfo.InstalledMarkItDownVersion, manifest)
                && comparison is not > 0)
            {
                logger.LogInformation("MarkItDown {Version} is already installed and compatible. Nothing to do.", envInfo.InstalledMarkItDownVersion);
                Report(progress, $"MarkItDown {envInfo.InstalledMarkItDownVersion} is ready.");
                // The version is already in hand; asking again would start another interpreter.
                await environmentManager.EnsureFormatCatalogAsync(envInfo.InstalledMarkItDownVersion, cancellationToken);
                return SetupResult.AlreadyUpToDate(envInfo.InstalledMarkItDownVersion, manifest);
            }

            if (versionGate.IsCompatible(envInfo.InstalledMarkItDownVersion, manifest))
                logger.LogInformation(
                    "A newer validated MarkItDown is available ({Installed} -> {Target}). Upgrading.",
                    envInfo.InstalledMarkItDownVersion, manifest.StableVersion);
            else
                logger.LogWarning(
                    "Installed version {Installed} is not in the validated set. Upgrading to {Target}.",
                    envInfo.InstalledMarkItDownVersion, manifest.StableVersion);
            Report(progress, $"Updating MarkItDown to {manifest.StableVersion}...");
        }

        var targetVersion = versionGate.GetTargetVersion(manifest);
        Report(progress, $"Installing MarkItDown {targetVersion} (this may take a minute the first time)...");
        await environmentManager.SetupAsync(targetVersion, forceReinstall, progress, cancellationToken);
        Report(progress, $"MarkItDown {targetVersion} installed.");
        await environmentManager.EnsureFormatCatalogAsync(targetVersion, cancellationToken);

        return SetupResult.Installed(targetVersion, manifest);
    }

    private static void Report(IProgress<string>? progress, string message) => progress?.Report(message);
}

public sealed class SetupResult
{
    public bool WasInstalled { get; private init; }
    public string Version { get; private init; } = string.Empty;

    /// <summary>
    /// The manifest this run went by. Handed back rather than acted on here, because what to do with
    /// it depends on who is asking: the desktop app shows a bar, the CLI prints a line.
    /// </summary>
    public CompatibilityManifest Manifest { get; private init; } = new();

    public static SetupResult Installed(string version, CompatibilityManifest manifest) =>
        new() { WasInstalled = true, Version = version, Manifest = manifest };

    public static SetupResult AlreadyUpToDate(string version, CompatibilityManifest manifest) =>
        new() { WasInstalled = false, Version = version, Manifest = manifest };
}
