using MdPipe.Core.Models;

namespace MdPipe.Core.Interfaces;

public interface IPythonEnvironmentManager
{
    Task<PythonEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default);
    Task SetupAsync(string markItDownVersion, bool forceReinstall = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure the record of what the engine can read matches the engine that is actually installed,
    /// refreshing it when it is missing or left over from another version.
    /// </summary>
    /// <param name="installedVersion">The version the caller already looked up, so this doesn't start
    /// another interpreter to ask the same question. Null means "find out".</param>
    Task EnsureFormatCatalogAsync(string? installedVersion = null, CancellationToken cancellationToken = default);
}
