using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Python;

public sealed class PythonEnvironmentManager(
    ILogger<PythonEnvironmentManager> logger,
    IHttpClientFactory httpClientFactory) : IPythonEnvironmentManager
{
    private const string EmbeddedPythonVersion = "3.12.7";

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpipe");

    private static readonly string VenvRoot = Path.Combine(Root, "venv");
    private static readonly string EmbedRoot = Path.Combine(Root, "python");

    private static string VenvPython => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(VenvRoot, "Scripts", "python.exe")
        : Path.Combine(VenvRoot, "bin", "python");

    private static string EmbedPython => Path.Combine(EmbedRoot, "python.exe");

    private static string? ReadyPython =>
        File.Exists(VenvPython) ? VenvPython :
        File.Exists(EmbedPython) ? EmbedPython : null;

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
                MissingReason = "The Python environment isn't usable (too old or incomplete); it will be rebuilt."
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
            TryDeleteDir(VenvRoot);
            TryDeleteDir(EmbedRoot);
        }
        else
        {
            if (File.Exists(VenvPython) && !await IsHealthyAsync(VenvPython, cancellationToken)) TryDeleteDir(VenvRoot);
            if (File.Exists(EmbedPython) && !await IsHealthyAsync(EmbedPython, cancellationToken)) TryDeleteDir(EmbedRoot);
        }

        var target = await EnsureInterpreterAsync(cancellationToken);

        logger.LogInformation("Installing markitdown[all]=={Version} into {Exe}", markItDownVersion, target);
        // Keep pip output so proxy, firewall and SSL failures reach the user.
        await RunProcessAsync(target, $"-m pip install \"markitdown[all]=={markItDownVersion}\" --disable-pip-version-check", cancellationToken);
        logger.LogInformation("Setup complete");
    }

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        var python = ReadyPython;
        return python is null ? null : await GetInstalledVersionAsync(python, cancellationToken);
    }

    internal string? GetPythonExecutable() => ReadyPython;

    private async Task<string> EnsureInterpreterAsync(CancellationToken cancellationToken)
    {
        if (ReadyPython is { } existing) return existing;

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

            // Microsoft Store Python can report a successful venv creation without placing the interpreter on disk.
            logger.LogWarning("The system Python did not produce a usable venv; using an embedded Python instead.");
            TryDeleteDir(VenvRoot);
        }

        await BootstrapEmbeddedPythonAsync(cancellationToken);

        if (!File.Exists(EmbedPython))
            throw new PythonEnvironmentException("Failed to set up the embedded Python environment.");

        return EmbedPython;
    }

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

        var zipPath = Path.Combine(EmbedRoot, "python-embed.zip");
        await DownloadFileAsync(http, zipUrl, zipPath, cancellationToken);
        ZipFile.ExtractToDirectory(zipPath, EmbedRoot, overwriteFiles: true);
        File.Delete(zipPath);

        if (!File.Exists(EmbedPython))
            throw new PythonEnvironmentException("The downloaded embedded Python package did not contain python.exe.");

        EnableEmbeddedSitePackages();

        var getPip = Path.Combine(EmbedRoot, "get-pip.py");
        await DownloadFileAsync(http, "https://bootstrap.pypa.io/get-pip.py", getPip, cancellationToken);
        await RunProcessAsync(EmbedPython, $"\"{getPip}\" --no-warn-script-location", cancellationToken);
        File.Delete(getPip);

        logger.LogInformation("Embedded Python ready at {Exe}", EmbedPython);
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string destPath, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var fileStream = File.Create(destPath);
            await response.Content.CopyToAsync(fileStream, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new PythonEnvironmentException(
                $"Couldn't download from {new Uri(url).Host}. A proxy, firewall, VPN or antivirus may be blocking it. " +
                $"Details: {ex.Message}", ex);
        }
    }

    // pip does not inherit Windows proxy settings, so child processes receive the detected proxy explicitly.
    private static readonly (string? Http, string? Https) SystemProxy = DetectSystemProxy();

    private static (string?, string?) DetectSystemProxy()
    {
        try
        {
            var proxy = HttpClient.DefaultProxy;
            string? ProxyFor(string url)
            {
                var uri = new Uri(url);
                return proxy is null || proxy.IsBypassed(uri) ? null : proxy.GetProxy(uri)?.AbsoluteUri;
            }
            return (ProxyFor("http://pypi.org"), ProxyFor("https://pypi.org"));
        }
        catch
        {
            return (null, null);
        }
    }

    private void EnableEmbeddedSitePackages()
    {
        var pth = Directory.GetFiles(EmbedRoot, "python*._pth").FirstOrDefault();
        if (pth is null)
        {
            logger.LogWarning("No ._pth file found in the embedded Python; site-packages may be disabled.");
            return;
        }

        var lines = File.ReadAllLines(pth).ToList();

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

    private async Task<string?> FindSystemPythonAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        foreach (var (exe, argPrefix) in LauncherCandidates)
        {
            try
            {
                var output = await RunProcessAsync(
                    exe,
                    argPrefix + "-c \"import sys,os,sysconfig; print(sys.executable if (sys.version_info >= (3,10) and os.path.isfile(os.path.join(sysconfig.get_paths()['stdlib'],'os.py'))) else '')\"",
                    cts.Token, captureOutput: true);

                var path = output.Trim();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    logger.LogInformation("Resolved system Python: {Path} (via '{Exe}')", path, exe);
                    return path;
                }

                logger.LogWarning("Ignoring '{Exe}': its Python is too old (need 3.10+) or has no usable standard library.", exe);
            }
            catch { }
        }

        return null;
    }

    private async Task<bool> IsHealthyAsync(string pythonExe, CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunProcessAsync(
                pythonExe,
                "-c \"import os,sys,sysconfig; z=os.path.join(os.path.dirname(sys.executable),f'python{sys.version_info.major}{sys.version_info.minor}.zip'); ok = sys.version_info >= (3,10) and (os.path.isfile(os.path.join(sysconfig.get_paths()['stdlib'],'os.py')) or os.path.isfile(z)); print('OK' if ok else '')\"",
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
        catch (IOException)
        {
            // A locked cleanup target should not abort setup.
        }
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
