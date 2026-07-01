using System.Reflection;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;

namespace MdPipe.Infrastructure.Manifest;

/// <summary>
/// Reads the compatibility manifest baked into the assembly at build time.
/// This is the offline baseline: MdPipe can always prepare a known-good MarkItDown
/// version even with no network and no published remote manifest.
/// </summary>
public sealed class EmbeddedManifestProvider : IManifestProvider
{
    // Logical resource name: "<RootNamespace>.<path with dots>".
    private const string ResourceName = "MdPipe.Infrastructure.Resources.markitdown-compat.json";

    public Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(EmbeddedManifestProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new ManifestException($"Embedded manifest resource '{ResourceName}' not found.");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return Task.FromResult(ManifestSerializer.Deserialize(json));
    }
}
