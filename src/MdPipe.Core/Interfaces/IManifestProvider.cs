using MdPipe.Core.Models;

namespace MdPipe.Core.Interfaces;

public interface IManifestProvider
{
    Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default);
}
