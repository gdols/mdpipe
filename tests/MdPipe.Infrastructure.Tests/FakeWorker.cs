using MdPipe.Core.Interfaces;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// A conversion worker that misbehaves on request: a throwaway script speaking the same line-based
/// protocol as the real one, which can be told to die, hang, or answer with rubbish. Python,
/// because that way the pipe, the encoding and the line buffering are the real ones. It never
/// imports MarkItDown, so it costs a process start and nothing else.
/// </summary>
public sealed class FakeWorker : IConversionWorkerSource, IDisposable
{
    private readonly string _folder;

    private FakeWorker(string folder, string script)
    {
        _folder = folder;
        ScriptPath = Path.Combine(folder, "fake_worker.py");
        File.WriteAllText(ScriptPath, Preamble + script);
    }

    public string? PythonExecutable { get; init; } = Python.Executable;

    private string ScriptPath { get; }

    public string EnsureWorkerScript() => ScriptPath;

    /// <summary>
    /// What the behaviours below share. The reply carries the process id so a test can tell whether
    /// it is still talking to the same worker.
    /// </summary>
    private const string Preamble =
        """
        import json, os, sys, time

        sys.stdout.reconfigure(encoding="utf-8", newline="\n")
        sys.stdin.reconfigure(encoding="utf-8")

        def reply(**payload):
            payload.setdefault("pid", os.getpid())
            sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
            sys.stdout.flush()

        def paths():
            for line in sys.stdin:
                line = line.strip()
                if line:
                    yield line

        """;

    /// <summary>Converts everything it is given, reporting which process did it.</summary>
    public static FakeWorker WellBehaved(string folder) => new(folder,
        """
        for path in paths():
            reply(path=path, ok=True, markdown="# " + os.path.basename(path))
        """);

    /// <summary>Converts, but with content worth checking on the way out.</summary>
    public static FakeWorker Returning(string folder, string markdown) => new(folder,
        $"""
        for path in paths():
            reply(path=path, ok=True, markdown={ToPythonString(markdown)})
        """);

    /// <summary>Refuses every document, the way a real converter reports a file it cannot read.</summary>
    public static FakeWorker Refusing(string folder, string reason) => new(folder,
        $"""
        for path in paths():
            reply(path=path, ok=False, error={ToPythonString(reason)})
        """);

    /// <summary>Answers normally until the given file, then kills the interpreter outright.</summary>
    public static FakeWorker DyingOn(string folder, string fileName) => new(folder,
        $"""
        for path in paths():
            if os.path.basename(path) == {ToPythonString(fileName)}:
                sys.stderr.write("Fatal Python error: pretend segfault\n")
                sys.stderr.flush()
                os._exit(3)
            reply(path=path, ok=True, markdown="# " + os.path.basename(path))
        """);

    /// <summary>Answers normally until the given file, then simply stops answering.</summary>
    public static FakeWorker HangingOn(string folder, string fileName) => new(folder,
        $"""
        for path in paths():
            if os.path.basename(path) == {ToPythonString(fileName)}:
                time.sleep(600)
            reply(path=path, ok=True, markdown="# " + os.path.basename(path))
        """);

    /// <summary>Replies with something that is not JSON at all.</summary>
    public static FakeWorker Babbling(string folder) => new(folder,
        """
        for path in paths():
            sys.stdout.write("this is not json\n")
            sys.stdout.flush()
        """);

    /// <summary>Escapes a value for embedding in the generated script.</summary>
    private static string ToPythonString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (Exception) { }
    }
}
