namespace MdPipe.Core.Interfaces;

/// <summary>
/// The two things needed to start a conversion worker: an interpreter, and the script it runs.
/// </summary>
/// <remarks>
/// A seam rather than a design flourish. The converter is the part of MdPipe that talks to another
/// process and has to survive that process misbehaving: dying halfway through a batch, going quiet,
/// answering with something unreadable. None of that could be exercised while the only way to get a
/// worker was to install several hundred megabytes of Python packages and hope one of them broke on
/// cue. Behind this interface a test can supply a worker that misbehaves on purpose.
/// </remarks>
public interface IConversionWorkerSource
{
    /// <summary>The interpreter to run, or null when the environment has not been set up yet.</summary>
    string? PythonExecutable { get; }

    /// <summary>Writes the worker script where the interpreter can reach it, and says where that is.</summary>
    string EnsureWorkerScript();
}
