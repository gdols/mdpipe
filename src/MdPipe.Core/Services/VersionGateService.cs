using MdPipe.Core.Exceptions;
using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

public sealed class VersionGateService
{
    public bool IsCompatible(string installedVersion, CompatibilityManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
            return false;

        return manifest.CompatibleVersions.Contains(installedVersion, StringComparer.OrdinalIgnoreCase);
    }

    public void ThrowIfIncompatible(string installedVersion, CompatibilityManifest manifest)
    {
        if (!IsCompatible(installedVersion, manifest))
            throw new VersionGateException(
                $"MarkItDown {installedVersion} is not in the validated set. " +
                $"Safe version: {manifest.StableVersion}. Run 'mdpipe setup' to update.",
                installedVersion,
                manifest.StableVersion);
    }

    public string GetTargetVersion(CompatibilityManifest manifest) => manifest.StableVersion;
}
