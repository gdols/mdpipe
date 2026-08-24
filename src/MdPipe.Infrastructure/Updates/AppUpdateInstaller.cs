using System.Security.Cryptography;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Updates;

/// <inheritdoc />
public sealed class AppUpdateInstaller : IAppUpdateInstaller
{
    private readonly ILogger<AppUpdateInstaller> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _executablePath;

    /// <param name="executablePath">Which file to replace. Overridable so tests can exercise the
    /// swap against a throwaway file instead of the process running them.</param>
    public AppUpdateInstaller(
        ILogger<AppUpdateInstaller> logger,
        IHttpClientFactory httpClientFactory,
        string? executablePath = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _executablePath = executablePath;
    }

    /// <summary>
    /// Marks the executable being replaced. Windows allows renaming one that is running but not
    /// deleting it, so the outgoing version waits here until the next launch.
    /// </summary>
    private const string RetiredSuffix = ".old";

    /// <summary>
    /// Where the running executable is. Deliberately not AppContext.BaseDirectory, which for a
    /// single-file build points at the temporary folder it unpacked itself into.
    /// </summary>
    private string? CurrentExecutable => _executablePath ?? Environment.ProcessPath;

    public bool CanInstall
    {
        get
        {
            if (CurrentExecutable is not { } exe) return false;
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;

            // Asked rather than assumed: a portable executable can be on read-only media, in
            // Program Files, or on a share. Finding out after downloading 60 MB is worse than not
            // offering it. There is no way to test this other than writing something.
            var folder = Path.GetDirectoryName(exe);
            if (folder is null) return false;

            var probe = Path.Combine(folder, $".mdpipe-write-test-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllBytes(probe, []);
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.LogInformation("Cannot update in place: {Folder} is not writable.", folder);
                return false;
            }
        }
    }

    public async Task<string> InstallAsync(
        AppUpdate update, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
            throw new AppUpdateException("This release does not say where to download it from.");

        if (CurrentExecutable is not { } exe)
            throw new AppUpdateException("Could not work out which file MdPipe is running from.");

        var folder = Path.GetDirectoryName(exe)!;
        var incoming = Path.Combine(folder, $"MdPipe-{update.Version}.download");

        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(15);

            // The hash first. Downloading 60 MB only to find there is nothing to check it against
            // wastes the user's time, and an unverified executable is not going to be run anyway.
            var expected = await ExpectedHashAsync(http, update.DownloadUrl, cancellationToken);

            progress?.Report("Downloading MdPipe " + update.Version + "...");
            await DownloadAsync(http, update.DownloadUrl, incoming, progress, cancellationToken);

            progress?.Report("Checking the download...");
            var actual = await HashAsync(incoming, cancellationToken);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Hash mismatch: expected {Expected}, got {Actual}.", expected, actual);
                throw new AppUpdateException(
                    "The downloaded file does not match what the release says it should be, so it was not installed.");
            }

            // Everything above can fail without consequence. Past this line the running executable
            // moves, so it happens last and in one step.
            var retired = exe + RetiredSuffix;
            TryDelete(retired);
            File.Move(exe, retired);

            try
            {
                File.Move(incoming, exe);
            }
            catch
            {
                // Putting the old one back matters more than reporting the failure neatly: leaving
                // no executable at all would be the one genuinely unrecoverable outcome.
                File.Move(retired, exe);
                throw;
            }

            _logger.LogInformation("Updated to MdPipe {Version}.", update.Version);
            return exe;
        }
        catch (Exception ex) when (ex is not AppUpdateException and not OperationCanceledException)
        {
            throw new AppUpdateException($"The update could not be installed: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(incoming);
        }
    }

    public void CleanUpPreviousUpdate()
    {
        if (CurrentExecutable is not { } exe) return;

        var retired = exe + RetiredSuffix;
        if (File.Exists(retired) && TryDelete(retired))
            _logger.LogInformation("Removed the previous version left behind by an update.");
    }

    /// <summary>
    /// Reads the hash published next to the release asset. Its absence stops the update: running an
    /// executable nobody checked is worse than making the user fetch it from the browser, which at
    /// least brings SmartScreen along.
    /// </summary>
    private static async Task<string> ExpectedHashAsync(
        HttpClient http, string downloadUrl, CancellationToken cancellationToken)
    {
        try
        {
            var text = await http.GetStringAsync(downloadUrl + ".sha256", cancellationToken);

            // Accepts both a bare hash and the "<hash>  <filename>" that sha256sum writes.
            var hash = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                throw new AppUpdateException("The published checksum is not readable.");

            return hash;
        }
        catch (HttpRequestException ex)
        {
            throw new AppUpdateException(
                "Could not fetch the checksum for this release, so the download could not be verified.", ex);
        }
        // A timeout arrives as a cancellation, the same trap as the manifest fetch. The caller's own
        // cancellation is left alone.
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppUpdateException("Timed out fetching the checksum for this release.", ex);
        }
    }

    private static async Task DownloadAsync(
        HttpClient http, string url, string destination, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long received = 0;
        var lastReported = -1;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;

            if (total is not > 0) continue;

            var percent = (int)(received * 100 / total.Value);
            if (percent < lastReported + 5) continue;

            lastReported = percent;
            progress?.Report($"Downloading MdPipe... {percent}%");
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Expected while the old executable is still running. Next launch gets it.
            return false;
        }
    }
}
