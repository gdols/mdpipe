using System.Text.RegularExpressions;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Models;

namespace MdPipe.Core.Services;

public sealed partial class VersionGateService
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

    /// <summary>
    /// Compares two PyPI-style version strings ("0.1.7", "0.1.5b1", "0.1.7.post1").
    /// Returns negative, zero or positive like a regular comparer, or null when either
    /// side doesn't follow the scheme, so the caller can log it instead of guessing.
    /// Ordering within one release number: a &lt; b &lt; rc &lt; final &lt; post.
    /// </summary>
    public int? Compare(string left, string right)
    {
        if (!TryParse(left, out var l) || !TryParse(right, out var r))
            return null;

        var releaseParts = Math.Max(l.Release.Length, r.Release.Length);
        for (var i = 0; i < releaseParts; i++)
        {
            var a = i < l.Release.Length ? l.Release[i] : 0;
            var b = i < r.Release.Length ? r.Release[i] : 0;
            if (a != b) return a.CompareTo(b);
        }

        if (l.PhaseRank != r.PhaseRank) return l.PhaseRank.CompareTo(r.PhaseRank);
        return l.PhaseNumber.CompareTo(r.PhaseNumber);
    }

    private readonly record struct ParsedVersion(int[] Release, int PhaseRank, int PhaseNumber);

    // Accepts the subset of PEP 440 that shows up in practice: a dotted release number,
    // optionally followed by a pre-release (a/b/rc) OR a .postN suffix. "v" prefix tolerated.
    [GeneratedRegex(@"^v?(\d+(?:\.\d+)*)(?:(a|b|rc)(\d+))?(?:\.?post(\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    private static bool TryParse(string version, out ParsedVersion parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(version)) return false;

        var match = VersionPattern().Match(version.Trim());
        if (!match.Success) return false;

        // A pre-release of a post-release ("0.1.5b1.post2") doesn't fit this flat model;
        // better to say "can't compare" than to order it wrong.
        if (match.Groups[2].Success && match.Groups[4].Success) return false;

        int[] release;
        try
        {
            release = match.Groups[1].Value.Split('.').Select(int.Parse).ToArray();
        }
        catch (OverflowException)
        {
            return false;
        }

        // Phase ranks: a=0, b=1, rc=2, final=3, post=4.
        var (rank, number) = (3, 0);
        if (match.Groups[2].Success)
        {
            rank = match.Groups[2].Value.ToLowerInvariant() switch { "a" => 0, "b" => 1, _ => 2 };
            number = int.Parse(match.Groups[3].Value);
        }
        else if (match.Groups[4].Success)
        {
            (rank, number) = (4, int.Parse(match.Groups[4].Value));
        }

        parsed = new ParsedVersion(release, rank, number);
        return true;
    }
}
