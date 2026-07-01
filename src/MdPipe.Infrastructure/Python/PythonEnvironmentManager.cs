using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Python;

/// <summary>
/// Owns MdPipe's private Python environment. Strategy, in order of preference:
///   1. A healthy system Python (creates a fast, shared venv under AppData).
///   2. A self-contained <b>embedded Python</b> downloaded into MdPipe's own folder, used when no usable
///      system Python exists or it cannot create a venv (e.g. the Microsoft Store edition).
/// MdPipe never installs into, modifies, or repairs the system Python — everything lives locally so it
/// cannot disturb other environments.
/// </summary>
public sealed class PythonEnvironmentManager(
    ILogger<PythonEnvironmentManager> logger,
    IHttpClientFactory httpClientFactory) : IPythonEnvironmentManager
{
    // Version of the official Windows "embeddable" Python used when we must bring our own.
    private const string EmbeddedPythonVersion = "3.12.7";

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpipe");

    private static readonly string VenvRoot = Path.Combine(Root, "venv");
    private static readonly string EmbedRoot = Path.Combine(Root, "python");

    private static string VenvPython => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(VenvRoot, "Scripts", "python.exe")
        : Path.Combine(VenvRoot, "bin", "python");

    private static string EmbedPython => Path.Combine(EmbedRoot, "python.exe");

    /// <summary>The interpreter MdPipe should use right now, or null if none is set up yet.</summary>
    private static string? ReadyPython =>
        File.Exists(VenvPython) ? VenvPython :
        File.Exists(EmbedPython) ? EmbedPython : null;

    /// <summary>Candidate launchers, in order of preference, as (executable, argPrefix).</summary>
    private static IEnumerable<(string Exe, string ArgPrefix)> LauncherCandidates =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? [("py", "-3 "), ("python", ""), ("python3", "")]   // 'py' launcher is never a Store stub
            : [("python3", ""), ("python", "")];

    public async Task<PythonEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default)
    {
        var python = ReadyPython;
        if (python is null)
            return new PythonEnvironmentInfo { IsReady = false, MissingReason = "Python environment not set up yet. Run 'mdpipe setup'." };

        if (!await IsHealthyAsync(python, cancellationToken))
            return new PythonEnvironmentInfo
            {
                IsReady = false,
                PythonExecutable = python,
                MissingReason = "The Python environment is broken (standard library not found); it will be rebuilt."
            };

        var version = await GetInstalledVersionAsync(python, cancellationToken);
        if (version is null)
            return new PythonEnvironmentInfo { IsReady = false, PythonExecutable = python, MissingReason = "MarkItDown is not installed in the environment." };

        return new PythonEnvironmentInfo
        {
            IsReady = true,
            PythonExecutable = python,
            InstalledMarkItDownVersion = version
        };
    }

    public async Task SetupAsync(string markItDownVersion, bool forceReinstall = false, CancellationToken cancellationToken = default)
    {
        if (forceReinstall)
        {
            // A clean reinstall: throw away whatever MdPipe created and rebuild from scratch.
            TryDeleteDir(VenvRoot);
            TryDeleteDir(EmbedRoot);
        }
        else
        {
            // Drop a broken environment (e.g. a base Python that lost its standard library) so it rebuilds.
            if (File.Exists(VenvPython) && !await IsHealthyAsync(VenvPython, cancellationToken)) TryDeleteDir(VenvRoot);
            if (File.Exists(EmbedPython) && !await IsHealthyAsync(EmbedPython, cancellationToken)) TryDeleteDir(EmbedRoot);
        }

        var target = await EnsureInterpreterAsync(cancellationToken);

        // Install the [all] extra so every converter works (PDF, Word, Excel, PowerPoint, audio…).
        // The base 'markitdown' package ships without the optional format dependencies.
        logger.LogInformation("Installing markitdown[all]=={Version} into {Exe}", markItDownVersion, target);
        await RunProcessAsync(target, $"-m pip install \"markitdown[all]=={markItDownVersion}\" --quiet", cancellationToken);
        logger.LogInformation("Setup complete");
    }

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        var python = ReadyPython;
        return python is null ? null : await GetInstalledVersionAsync(python, cancellationToken);
    }

    internal string? GetPythonExecutable() => ReadyPython;

    /// <summary>
    /// Returns a ready-to-use interpreter, creating one if needed: a venv from a healthy system Python, or
    /// — failing that — a private embedded Python downloaded into MdPipe's own folder.
    /// </summary>
    private async Task<string> EnsureInterpreterAsync(CancellationToken cancellationToken)
    {
        if (ReadyPython is { } existing) return existing;

        // Prefer a healthy system Python (fast, shared on disk).
        var systemPython = await FindSystemPythonAsync(cancellationToken);
        if (systemPython is not null)
        {
            logger.LogInformation("Creating virtual environment at {VenvRoot} using {Python}", VenvRoot, systemPython);
            Directory.CreateDirectory(Root);
            try
            {
                await RunProcessAsync(systemPython, $"-m venv \"{VenvRoot}\"", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Creating a venv from the system Python failed; falling back to an embedded Python.");
            }

            if (File.Exists(VenvPython)) return VenvPython;

            // The Store edition of Python virtualises file writes when spawned from a background process, so
            // `-m venv` reports success yet the interpreter never lands on disk. Clean up and bring our own.
            logger.LogWarning("The system Python did not produce a usable venv; using an embedded Python instead.");
            TryDeleteDir(VenvRoot);
        }

        await BootstrapEmbeddedPythonAsync(cancellationToken);

        if (!File.Exists(EmbedPython))
            throw new PythonEnvironmentException("Failed to set up the embedded Python environment.");

        return EmbedPython;
    }

    /// <summary>
    /// Downloads the official Windows embeddable Python into <see cref="EmbedRoot"/>, enables site-packages,
    /// and bootstraps pip — yielding a fully self-contained interpreter MdPipe controls end to end.
    /// </summary>
    private async Task BootstrapEmbeddedPythonAsync(CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PythonNotFoundException("No usable Python was found. Please install Python 3.9 or later.");

        logger.LogInformation("Setting up a private embedded Python at {Root}", EmbedRoot);
        TryDeleteDir(EmbedRoot);
        Directory.CreateDirectory(EmbedRoot);

        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        var zipUrl = $"https://www.python.org/ftp/python/{EmbeddedPythonVersion}/python-{EmbeddedPythonVersion}-embed-{arch}.zip";

        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);

        // 1. Download and unpack the embeddable interpreter.
        var zipPath = Path.Combine(EmbedRoot, "python-embed.zip");
        await DownloadFileAsync(http, zipUrl, zipPath, cancellationToken);
        ZipFile.ExtractToDirectory(zipPath, EmbedRoot, overwriteFiles: true);
        File.Delete(zipPath);

        if (!File.Exists(EmbedPython))
            throw new PythonEnvironmentException("The downloaded embedded Python package did not contain python.exe.");

        // 2. The embeddable ships with site-packages disabled; turn it on so pip-installed packages import.
        EnableEmbeddedSitePackages();

        // 3. Bootstrap pip into the embedded interpreter.
        var getPip = Path.Combine(EmbedRoot, "get-pip.py");
        await DownloadFileAsync(http, "https://bootstrap.pypa.io/get-pip.py", getPip, cancellationToken);
        await RunProcessAsync(EmbedPython, $"\"{getPip}\" --no-warn-script-location", cancellationToken);
        File.Delete(getPip);

        logger.LogInformation("Embedded Python ready at {Exe}", EmbedPython);
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string destPath, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var fileStream = File.Create(destPath);
        await response.Content.CopyToAsync(fileStream, cancellationToken);
    }

    /// <summary>Rewrites the embeddable's <c>python*._pth</c> so it loads site-packages and runs site init.</summary>
    private void EnableEmbeddedSitePackages()
    {
        var pth = Directory.GetFiles(EmbedRoot, "python*._pth").FirstOrDefault();
        if (pth is null)
        {
            logger.LogWarning("No ._pth file found in the embedded Python; site-packages may be disabled.");
            return;
        }

        var lines = File.ReadAllLines(pth).ToList();

        // Un-comment the (commented-out) 'import site' directive.
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Trim().TrimStart('#').Trim() == "import site")
                lines[i] = "import site";

        const string sitePackages = "Lib\\site-packages";
        if (!lines.Any(l => l.Trim().Equals(sitePackages, StringComparison.OrdinalIgnoreCase)))
            lines.Add(sitePackages);
        if (!lines.Any(l => l.Trim() == "import site"))
            lines.Add("import site");

        File.WriteAllLines(pth, lines);
    }

    private async Task<string?> GetInstalledVersionAsync(string python, CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunProcessAsync(python, "-m pip show markitdown", cancellationToken, captureOutput: true);
            return ParseVersionFromPipShow(output);
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseVersionFromPipShow(string pipOutput)
    {
        foreach (var line in pipOutput.Split('\n'))
        {
            if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                return line["Version:".Length..].Trim();
        }
        return null;
    }

    /// <summary>
    /// Resolves a healthy system Python via <c>sys.executable</c>. Querying it (rather than trusting the
    /// command name) sidesteps Windows Store execution-alias stubs, and the standard-library check rejects a
    /// relocated/partial install whose <c>Lib/</c> is gone (which would otherwise poison the venv).
    /// </summary>
    private async Task<string?> FindSystemPythonAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        foreach (var (exe, argPrefix) in LauncherCandidates)
        {
            try
            {
                // Only accept an interpreter that can locate a real on-disk standard library. A relocated or
                // partially-uninstalled Python still prints sys.executable and exits 0 — so validate here.
                var output = await RunProcessAsync(
                    exe,
                    argPrefix + "-c \"import sys,os,sysconfig; print(sys.executable if os.path.isfile(os.path.join(sysconfig.get_paths()['stdlib'],'os.py')) else '')\"",
                    cts.Token, captureOutput: true);

                var path = output.Trim();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    logger.LogInformation("Resolved system Python: {Path} (via '{Exe}')", path, exe);
                    return path;
                }

                logger.LogWarning("Ignoring '{Exe}': its Python has no usable standard library.", exe);
            }
            catch { /* not available or stub failed — try the next candidate */ }
        }

        return null;
    }

    /// <summary>
    /// True only if the interpreter has a standard library it can actually reach — either on disk
    /// (Lib/os.py, for normal installs and venvs) or in the zip next to python.exe (for the embeddable).
    /// Catches a base Python that was moved or partially uninstalled — symptom: "Could not find platform
    /// independent libraries" plus conversions that work from some working directories but fail from others.
    /// </summary>
    private async Task<bool> IsHealthyAsync(string pythonExe, CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunProcessAsync(
                pythonExe,
                "-c \"import os,sys,sysconfig; z=os.path.join(os.path.dirname(sys.executable),f'python{sys.version_info.major}{sys.version_info.minor}.zip'); print('OK' if (os.path.isfile(os.path.join(sysconfig.get_paths()['stdlib'],'os.py')) or os.path.isfile(z)) else '')\"",
                cancellationToken, captureOutput: true);
            return output.Trim() == "OK";
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort — a locked file shouldn't abort setup */ }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task<string> RunProcessAsync(
        string executable, string arguments, CancellationToken cancellationToken, bool captureOutput = false)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new PythonEnvironmentException($"Failed to start process: {executable}");

        var outputTask = captureOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var err = await errorTask;
            throw new PythonEnvironmentException($"Process '{executable} {arguments}' failed (exit {process.ExitCode}): {err}");
        }

        return await outputTask;
    }
}
