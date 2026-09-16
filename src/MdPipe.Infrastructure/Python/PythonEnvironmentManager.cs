using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Python;

public sealed class PythonEnvironmentManager : IPythonEnvironmentManager, IConversionWorkerSource
{
    private const string EmbeddedPythonVersion = "3.12.7";

    /// <summary>
    /// The Python versions MarkItDown can actually be installed on: 3.10 up to but not including
    /// 3.14. The floor is MarkItDown's own; the ceiling belongs to its dependencies, which is the
    /// less obvious half. markitdown[all] 0.1.7 pins youtube-transcript-api~=1.0.0 and every version
    /// in that range declares &lt;3.14, so on a 3.14 machine pip finds nothing and gives up.
    /// Raise it when a MarkItDown release supports a newer Python, like EmbeddedPythonVersion.
    /// </summary>
    private static readonly (int Major, int Minor) OldestUsablePython = (3, 10);
    private static readonly (int Major, int Minor) FirstUnusablePython = (3, 14);

    private static string VersionIsInRange =>
        $"({OldestUsablePython.Major},{OldestUsablePython.Minor}) <= sys.version_info[:2] < " +
        $"({FirstUnusablePython.Major},{FirstUnusablePython.Minor})";

    private static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpipe");

    private readonly ILogger<PythonEnvironmentManager> logger;
    private readonly IHttpClientFactory httpClientFactory;

    /// <param name="root">The folder MdPipe keeps its environment in. Overridable so tests work
    /// against a throwaway directory instead of the real one under AppData.</param>
    public PythonEnvironmentManager(
        ILogger<PythonEnvironmentManager> logger,
        IHttpClientFactory httpClientFactory,
        string? root = null)
    {
        this.logger = logger;
        this.httpClientFactory = httpClientFactory;
        Root = root ?? DefaultRoot;
    }

    private string Root { get; }

    private string VenvRoot => Path.Combine(Root, "venv");
    private string EmbedRoot => Path.Combine(Root, "python");

    private string VenvPython => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(VenvRoot, "Scripts", "python.exe")
        : Path.Combine(VenvRoot, "bin", "python");

    private string EmbedPython => Path.Combine(EmbedRoot, "python.exe");

    /// <summary>
    /// The venv built on a system Python when there is one, otherwise the Python MdPipe downloaded
    /// for itself.
    /// </summary>
    private string? ReadyPython =>
        File.Exists(VenvPython) ? VenvPython :
        File.Exists(EmbedPython) ? EmbedPython : null;

    /// <summary>
    /// Where to look for a system Python. On Windows the py launcher is asked for each usable version
    /// by name, newest first: a bare "py -3" picks the newest installed, so somebody with 3.14 and
    /// 3.12 side by side would get the one that can't be used and download a Python they already
    /// have. The launcher is also never the Store stub, unlike a bare python on PATH.
    /// </summary>
    internal static IEnumerable<(string Exe, string ArgPrefix)> LauncherCandidates =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? [.. UsableMinorsNewestFirst.Select(minor => ("py", $"-{OldestUsablePython.Major}.{minor} ")),
               ("python", ""), ("python3", "")]
            : [("python3", ""), ("python", "")];

    private static IEnumerable<int> UsableMinorsNewestFirst =>
        Enumerable.Range(OldestUsablePython.Minor, FirstUnusablePython.Minor - OldestUsablePython.Minor).Reverse();

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

    public async Task SetupAsync(string markItDownVersion, bool forceReinstall = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        // Anything remembered about an interpreter stops being true once it is deleted or replaced.
        lock (_healthChecked) _healthChecked.Clear();

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

        var target = await EnsureInterpreterAsync(progress, cancellationToken);

        try
        {
            await InstallMarkItDownAsync(target, markItDownVersion, progress, cancellationToken);
        }
        catch (PythonEnvironmentException) when (target == VenvPython)
        {
            // The interpreter ran, so this is not a broken Python, it is one MarkItDown will not
            // install on. The version check above catches the case we know about; this catches the
            // next one, which by definition we do not.
            logger.LogWarning(
                "MarkItDown would not install on the system Python. Falling back to the bundled one.");
            progress?.Report("That Python did not work out. Trying with the one MdPipe brings...");

            TryDeleteDir(VenvRoot);
            await BootstrapEmbeddedPythonAsync(progress, cancellationToken);

            if (!File.Exists(EmbedPython))
                throw new PythonEnvironmentException("Failed to set up the embedded Python environment.");

            await InstallMarkItDownAsync(EmbedPython, markItDownVersion, progress, cancellationToken);
        }

        EnsureWorkerScript();
        logger.LogInformation("Setup complete");
    }

    private async Task InstallMarkItDownAsync(
        string python, string version, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Installing markitdown[all]=={Version} into {Exe}", version, python);
        progress?.Report($"Downloading MarkItDown {version} and its converters (the biggest part)...");

        try
        {
            // pip's output is kept so proxy, firewall and SSL failures reach the user.
            await RunProcessAsync(
                python, $"-m pip install \"markitdown[all]=={version}\" --disable-pip-version-check",
                cancellationToken);
        }
        catch (PythonEnvironmentException ex) when (LooksLikeAVersionClash(ex.Message))
        {
            // Telling somebody to check their firewall when the problem is their Python sends them
            // looking in entirely the wrong place.
            throw new PythonEnvironmentException(
                $"MarkItDown {version} cannot be installed on this Python. Its own dependencies do not " +
                "support that version yet. This is not a problem with your network or your computer.\n\n" +
                ex.Message, ex);
        }
    }

    /// <summary>
    /// Whether pip gave up because nothing satisfied the requirements, rather than because it could
    /// not reach anything. The two look identical in a dialog box and lead somewhere completely
    /// different, and the reflex is to blame the network.
    /// </summary>
    internal static bool LooksLikeAVersionClash(string pipOutput) =>
        pipOutput.Contains("require a different python version", StringComparison.OrdinalIgnoreCase) ||
        pipOutput.Contains("No matching distribution found", StringComparison.OrdinalIgnoreCase) ||
        pipOutput.Contains("Could not find a version that satisfies", StringComparison.OrdinalIgnoreCase);

    public async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        var python = ReadyPython;
        return python is null ? null : await GetInstalledVersionAsync(python, cancellationToken);
    }

    /// <inheritdoc />
    public string? PythonExecutable => ReadyPython;

    private const string WorkerResourceName = "MdPipe.Infrastructure.Resources.worker.py";
    private string WorkerScript => Path.Combine(Root, "worker.py");

    /// <summary>
    /// Drops the bundled worker next to the environment, rewriting it whenever it is missing or
    /// stale so an updated MdPipe never talks to an old script.
    /// </summary>
    /// <inheritdoc />
    public string EnsureWorkerScript()
    {
        var expected = ReadEmbeddedWorker();

        if (!File.Exists(WorkerScript) || File.ReadAllText(WorkerScript) != expected)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(WorkerScript, expected);
            logger.LogInformation("Wrote the conversion worker to {Path}", WorkerScript);
        }

        return WorkerScript;
    }

    private string FormatCatalogPath => Path.Combine(Root, "formats.json");

    /// <summary>
    /// Asks MarkItDown what it can read and stores the answer, so MdPipe reports the truth about this
    /// machine instead of a list somebody typed out once. Skipped when the answer on disk already
    /// belongs to the installed version, since asking costs a Python start-up.
    /// </summary>
    public async Task EnsureFormatCatalogAsync(
        string? installedVersion = null, CancellationToken cancellationToken = default)
    {
        var pythonExe = ReadyPython;
        if (pythonExe is null) return;

        try
        {
            var installed = installedVersion ?? await GetInstalledVersionAsync(pythonExe, cancellationToken);
            if (installed is not null && File.Exists(FormatCatalogPath) &&
                (await File.ReadAllTextAsync(FormatCatalogPath, cancellationToken)).Contains($"\"{installed}\""))
                return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through and just rewrite it.
        }

        try
        {
            var json = await RunProcessAsync(
                pythonExe, $"\"{EnsureWorkerScript()}\" --formats", cancellationToken, captureOutput: true);

            var line = json.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith('{'))?.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                logger.LogWarning("The engine did not report its formats; the list already recorded stays in use.");
                return;
            }

            // No formats means broken discovery, not an engine that reads nothing. Writing that
            // would replace a good catalogue with an empty one. Keeping what is there is safer.
            if (!DescribesFormats(line, out var complaint))
            {
                logger.LogWarning(
                    "The engine could not say what it reads, so the list already recorded stays in use. {Reason}",
                    complaint);
                return;
            }

            Directory.CreateDirectory(Root);
            await File.WriteAllTextAsync(FormatCatalogPath, line, cancellationToken);
            logger.LogInformation("Recorded the engine's supported formats at {Path}", FormatCatalogPath);
        }
        catch (Exception ex) when (ex is PythonEnvironmentException or IOException or UnauthorizedAccessException)
        {
            // A cosmetic loss, not a reason to fail a setup that worked.
            logger.LogWarning(ex, "Could not record the engine's supported formats; the bundled list stays in use.");
        }
    }

    private static string ReadEmbeddedWorker()
    {
        using var stream = typeof(PythonEnvironmentManager).Assembly.GetManifestResourceStream(WorkerResourceName)
            ?? throw new PythonEnvironmentException($"Embedded resource '{WorkerResourceName}' is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task<string> EnsureInterpreterAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (ReadyPython is { } existing) return existing;

        progress?.Report("Preparing the Python environment...");
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

            // Store Python can report a successful venv without putting the interpreter on disk.
            logger.LogWarning("The system Python did not produce a usable venv; using an embedded Python instead.");
            TryDeleteDir(VenvRoot);
        }

        await BootstrapEmbeddedPythonAsync(progress, cancellationToken);

        if (!File.Exists(EmbedPython))
            throw new PythonEnvironmentException("Failed to set up the embedded Python environment.");

        return EmbedPython;
    }

    private async Task BootstrapEmbeddedPythonAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PythonNotFoundException("No usable Python was found. Please install Python 3.10 or later.");

        logger.LogInformation("Setting up a private embedded Python at {Root}", EmbedRoot);
        TryDeleteDir(EmbedRoot);
        Directory.CreateDirectory(EmbedRoot);

        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        var zipUrl = $"https://www.python.org/ftp/python/{EmbeddedPythonVersion}/python-{EmbeddedPythonVersion}-embed-{arch}.zip";

        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);

        var zipPath = Path.Combine(EmbedRoot, "python-embed.zip");
        await DownloadFileAsync(http, zipUrl, zipPath, $"Downloading Python {EmbeddedPythonVersion}", progress, cancellationToken);
        ZipFile.ExtractToDirectory(zipPath, EmbedRoot, overwriteFiles: true);
        File.Delete(zipPath);

        if (!File.Exists(EmbedPython))
            throw new PythonEnvironmentException("The downloaded embedded Python package did not contain python.exe.");

        EnableEmbeddedSitePackages();

        progress?.Report("Setting up pip...");
        var getPip = Path.Combine(EmbedRoot, "get-pip.py");
        await DownloadFileAsync(http, "https://bootstrap.pypa.io/get-pip.py", getPip, "Downloading pip", progress, cancellationToken);
        await RunProcessAsync(EmbedPython, $"\"{getPip}\" --no-warn-script-location", cancellationToken);
        File.Delete(getPip);

        logger.LogInformation("Embedded Python ready at {Exe}", EmbedPython);
    }

    private static async Task DownloadFileAsync(
        HttpClient http, string url, string destPath, string label, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            var sizeText = totalBytes is > 0 ? $" ({totalBytes.Value / (1024 * 1024)} MB)" : "";
            progress?.Report($"{label}{sizeText}...");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(destPath);

            var buffer = new byte[81920];
            long received = 0;
            var lastReported = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (totalBytes is > 0)
                {
                    var pct = (int)(received * 100 / totalBytes.Value);
                    if (pct >= lastReported + 5)
                    {
                        lastReported = pct;
                        progress?.Report($"{label}... {pct}%");
                    }
                }
            }
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException ||
            (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
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

    internal void EnableEmbeddedSitePackages()
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

    /// <summary>
    /// Whether the worker's reply is a usable catalogue. Discovery reaches into MarkItDown's private
    /// converter list, which can stop working in any release without anything else breaking, so the
    /// worker says so outright and this is where it is believed rather than written to disk.
    /// </summary>
    internal static bool DescribesFormats(string json, out string complaint)
    {
        complaint = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                complaint = error.GetString() ?? "The engine reported an error without saying what.";
                return false;
            }

            if (!root.TryGetProperty("extensions", out var extensions) ||
                extensions.ValueKind != JsonValueKind.Array ||
                extensions.GetArrayLength() == 0)
            {
                complaint = "The reply contained no formats at all.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            complaint = $"The reply could not be read: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// importlib.metadata rather than "pip show", which imports the whole of pip: 119 ms against
    /// 438 ms measured here. A missing package exits non-zero, which the catch turns into null.
    /// </summary>
    private async Task<string?> GetInstalledVersionAsync(string python, CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunProcessAsync(
                python,
                "-c \"import importlib.metadata as m; print(m.version('markitdown'))\"",
                cancellationToken, captureOutput: true);

            var version = output.Trim();
            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch
        {
            return null;
        }
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
                    argPrefix + $"-c \"import sys,os,sysconfig; print(sys.executable if ({VersionIsInRange} and os.path.isfile(os.path.join(sysconfig.get_paths()['stdlib'],'os.py'))) else '')\"",
                    cts.Token, captureOutput: true);

                var path = output.Trim();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    logger.LogInformation("Resolved system Python: {Path} (via '{Exe}')", path, exe);
                    return path;
                }

                logger.LogWarning(
                "Ignoring '{Exe}': its Python is outside {Oldest}.x to {Newest}.x, or has no usable standard library.",
                exe, $"{OldestUsablePython.Major}.{OldestUsablePython.Minor}",
                $"{FirstUnusablePython.Major}.{FirstUnusablePython.Minor - 1}");
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Health checks already done, remembered for the life of the process. A Python that was fine a
    /// second ago is still fine, and asking again costs another interpreter start.
    /// </summary>
    private readonly Dictionary<string, bool> _healthChecked = new(StringComparer.OrdinalIgnoreCase);

    private async Task<bool> IsHealthyAsync(string pythonExe, CancellationToken cancellationToken)
    {
        lock (_healthChecked)
            if (_healthChecked.TryGetValue(pythonExe, out var remembered)) return remembered;

        var healthy = await CheckHealthAsync(pythonExe, cancellationToken);

        lock (_healthChecked)
            _healthChecked[pythonExe] = healthy;

        return healthy;
    }

    private async Task<bool> CheckHealthAsync(string pythonExe, CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunProcessAsync(
                pythonExe,
                $"-c \"import os,sys,sysconfig; z=os.path.join(os.path.dirname(sys.executable),f'python{{sys.version_info.major}}{{sys.version_info.minor}}.zip'); ok = {VersionIsInRange} and (os.path.isfile(os.path.join(sysconfig.get_paths()['stdlib'],'os.py')) or os.path.isfile(z)); print('OK' if ok else '')\"",
                cancellationToken, captureOutput: true);
            return output.Trim() == "OK";
        }
        catch
        {
            return false;
        }
    }

    internal void TryDeleteDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;

            // The embeddable zip extracts some files read-only and Directory.Delete refuses those.
            // Clear the attribute first so a rebuild actually starts from scratch.
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Should not abort setup, but must not be invisible either.
            logger.LogWarning(ex, "Couldn't fully delete {Dir}; continuing with what's there.", dir);
        }
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

        // pip doesn't pick up the Windows proxy on its own; hand it over explicitly.
        if (SystemProxy.Http is { } httpProxy && !psi.Environment.ContainsKey("HTTP_PROXY"))
            psi.Environment["HTTP_PROXY"] = httpProxy;
        if (SystemProxy.Https is { } httpsProxy && !psi.Environment.ContainsKey("HTTPS_PROXY"))
            psi.Environment["HTTPS_PROXY"] = httpsProxy;

        using var process = Process.Start(psi)
            ?? throw new PythonEnvironmentException($"Failed to start process: {executable}");

        var outputTask = captureOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelling only stops the waiting. pip would carry on downloading into the folder a
            // retry deletes and rebuilds, so a second run would race a process nobody can see.
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            var err = await errorTask;
            throw new PythonEnvironmentException($"Process '{executable} {arguments}' failed (exit {process.ExitCode}): {err}");
        }

        return await outputTask;
    }
}
