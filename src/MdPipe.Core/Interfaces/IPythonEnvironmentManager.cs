using MdPipe.Core.Models;

namespace MdPipe.Core.Interfaces;

public interface IPythonEnvironmentManager
{
    Task<PythonEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default);
    Task SetupAsync(string markItDownVersion, bool forceReinstall = false, CancellationToken cancellationToken = default);
    Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default);
}
