namespace MdPipe.Core.Interfaces;

/// <summary>
/// The manifest baked into this build, as opposed to the one fetched from the repository.
/// </summary>
/// <remarks>
/// These are two different questions and they deserve two different answers.
/// <para>
/// <b>Which engine should be installed</b> is decided here, by the build. A release of MdPipe is a
/// combination of an application and a MarkItDown version that were tested together, and editing a
/// file in the repository should not be able to swap half of that out underneath somebody. It also
/// means the engine installs correctly with no network beyond PyPI itself.
/// </para>
/// <para>
/// <b>Whether a newer MdPipe exists</b> is the remote manifest's job, and the only one it still has.
/// </para>
/// </remarks>
public interface IBuildManifestProvider : IManifestProvider;
