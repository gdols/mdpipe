namespace MdPipe.Core.Models;

public sealed class CompatibilityManifest
{
    public int SchemaVersion { get; init; }
    public string StableVersion { get; init; } = string.Empty;
    public string MinimumVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> CompatibleVersions { get; init; } = [];
    public DateOnly UpdatedAt { get; init; }
    public string Notes { get; init; } = string.Empty;
}
