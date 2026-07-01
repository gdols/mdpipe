using MdPipe.Core.Exceptions;
using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

/// <summary>
/// The heart of MdPipe's "version gate": only let through MarkItDown versions we've actually vetted in
/// the manifest, so a surprise release upstream can't quietly break everyone's conversions overnight.
/// </summary>
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

    /// <summary>
    /// The version we install when setting up or upgrading — always the manifest's stableVersion,
    /// deliberately never "whatever happens to be newest on PyPI".
    /// </summary>
    public string GetTargetVersion(CompatibilityManifest manifest) => manifest.StableVersion;
}
