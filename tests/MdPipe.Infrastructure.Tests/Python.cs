using System.Diagnostics;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// Finds an interpreter for the tests that need a real worker process. The scripts involved import
/// nothing, so any Python will do. Tried in the same order the app uses, the py launcher first,
/// since a bare python on PATH is often the Store stub.
/// </summary>
public static class Python
{
    private static readonly Lazy<string?> Found = new(Locate);

    public static string? Executable => Found.Value;

    /// <summary>
    /// The interpreter, or a failure saying what is missing. Not a silent skip: a test that quietly
    /// does not run is worse than one that says why.
    /// </summary>
    public static string Required =>
        Executable ?? throw new InvalidOperationException(
            "These tests drive a real worker process and need Python on PATH. It imports nothing, so " +
            "any version will do. CI installs one; see CONTRIBUTING.md.");

    private static string? Locate()
    {
        foreach (var (exe, arguments) in new[] { ("py", "-3 -c \"import sys; print(sys.executable)\""), ("python", "-c \"import sys; print(sys.executable)\"") })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(exe, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null) continue;

                var path = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(10_000);

                if (process.ExitCode == 0 && path.Length > 0 && File.Exists(path)) return path;
            }
            catch (Exception)
            {
                // Not on this machine; try the next one.
            }
        }

        return null;
    }
}
