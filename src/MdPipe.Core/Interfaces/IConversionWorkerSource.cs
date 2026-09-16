namespace MdPipe.Core.Interfaces;

/// <summary>
/// The two things needed to start a conversion worker: an interpreter and the script it runs.
/// A seam, so tests can hand the converter a worker that misbehaves on purpose.
/// </summary>
public interface IConversionWorkerSource
{
    /// <summary>The interpreter to run, or null when the environment isn't set up yet.</summary>
    string? PythonExecutable { get; }

    /// <summary>Writes the worker script where the interpreter can reach it, and says where.</summary>
    string EnsureWorkerScript();
}
