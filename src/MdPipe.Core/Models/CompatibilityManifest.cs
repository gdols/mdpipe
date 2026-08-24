namespace MdPipe.Core.Models;

public sealed class CompatibilityManifest
{
    public int SchemaVersion { get; init; }
    public string StableVersion { get; init; } = string.Empty;
    public string MinimumVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> CompatibleVersions { get; init; } = [];
    public DateOnly UpdatedAt { get; init; }
    public string Notes { get; init; } = string.Empty;

    /// <summary>
    /// What the manifest says about MdPipe itself. Null on the older schema, and on the baseline
    /// baked into a build that predates it, so every reader has to cope with its absence.
    /// </summary>
    public AppRelease? App { get; init; }
}
