using System.Diagnostics;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// Finds an interpreter for the tests that need a real worker process.
/// </summary>
/// <remarks>
/// MdPipe converts by talking to Python, so testing that conversation with an actual interpreter is
/// the faithful thing rather than a convenience. The scripts involved import nothing, so any Python
/// will do and none of it costs the several hundred megabytes a real MarkItDown needs.
/// <para>
/// Tried in the same order the application itself uses, the <c>py</c> launcher first, since on
/// Windows a bare <c>python</c> on PATH is often the Store stub that answers questions and refuses
/// to do anything else.
/// </para>
/// </remarks>
public static class Python
{
    private static readonly Lazy<string?> Found = new(Locate);

    public static string? Executable => Found.Value;

    /// <summary>
    /// The interpreter, or a failure explaining what is missing. Deliberately not a silent skip: a
    /// test that quietly does not run is worse than one that says why, and CI installs Python for
    /// exactly this reason.
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
