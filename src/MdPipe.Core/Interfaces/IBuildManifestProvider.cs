namespace MdPipe.Core.Interfaces;

/// <summary>
/// The manifest baked into this build, which is what decides the MarkItDown version to install.
/// The remote manifest only says whether a newer MdPipe exists.
/// </summary>
public interface IBuildManifestProvider : IManifestProvider;
