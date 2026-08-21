using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Infrastructure.Python;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.MarkItDown;

/// <summary>
/// Converts documents by talking to a Python worker over stdin/stdout.
/// </summary>
/// <remarks>
/// Importing MarkItDown costs around two seconds and converting a small document around a tenth of
/// that, so the worker is started once per batch and fed one path at a time. What the process
/// boundary used to give us for free was isolation: a converter crashing took only its own file
/// down. That is restored deliberately here — the worker catches its own exceptions, and if the
/// interpreter dies outright, the file in flight is failed and the worker restarted for the rest.
/// </remarks>
public sealed class MarkItDownConverter(
    PythonEnvironmentManager environmentManager,
    ILogger<MarkItDownConverter> logger) : IMarkItDownConverter
{
    /// <summary>
    /// Deliberately generous: a large document can legitimately take minutes, so this is a "something
    /// is stuck" threshold rather than a performance budget. Without it a pathological file would hang
    /// the batch forever, which is exactly what used to happen.
    /// </summary>
    private static readonly TimeSpan PerFileTimeout = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default)
    {
        await foreach (var result in ConvertManyAsync([request], cancellationToken))
            return result;

        return ConversionResult.Fail("The conversion worker produced no result.");
    }

    public async IAsyncEnumerable<ConversionResult> ConvertManyAsync(
        IReadOnlyList<ConversionRequest> requests,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pythonExe = environmentManager.GetPythonExecutable();
        if (pythonExe is null)
        {
            foreach (var _ in requests)
                yield return ConversionResult.Fail("Python environment is not ready. Run 'mdpipe setup' first.");
            yield break;
        }

        var script = environmentManager.EnsureWorkerScript();
        Worker? worker = null;

        try
        {
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(request.SourcePath))
                {
                    yield return ConversionResult.Fail($"Source file not found: {request.SourcePath}");
                    continue;
                }

                worker ??= Worker.Start(pythonExe, script);
                logger.LogInformation("Converting {File}", request.SourcePath);

                var (result, workerLost) = await ConvertOneAsync(worker, request, cancellationToken);

                if (workerLost)
                {
                    // The interpreter went down with the file. Start clean so the rest of the batch runs.
                    worker.Dispose();
                    worker = null;
                }

                yield return result;
            }
        }
        finally
        {
            worker?.Dispose();
        }
    }

    private async Task<(ConversionResult Result, bool WorkerLost)> ConvertOneAsync(
        Worker worker, ConversionRequest request, CancellationToken cancellationToken)
    {
        string? line;
        try
        {
            line = await worker.RequestAsync(request.SourcePath, PerFileTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Conversion of {File} timed out after {Minutes} minutes", request.SourcePath, PerFileTimeout.TotalMinutes);
            return (ConversionResult.Fail($"Timed out after {PerFileTimeout.TotalMinutes:0} minutes."), true);
        }

        if (line is null)
        {
            var detail = worker.LastError;
            logger.LogWarning("The conversion worker stopped while handling {File}. {Detail}", request.SourcePath, detail);
            return (ConversionResult.Fail(SummarizeError(detail, worker.ExitCode)), true);
        }

        WorkerResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<WorkerResponse>(line, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read the worker's reply for {File}", request.SourcePath);
            return (ConversionResult.Fail("The conversion worker sent an unreadable reply."), true);
        }

        if (response is null || !response.Ok)
        {
            var reason = response?.Error ?? "unknown error";
            logger.LogWarning("Conversion failed for {File}: {Reason}", request.SourcePath, reason);
            return (ConversionResult.Fail($"MarkItDown could not convert the file: {reason}"), false);
        }

        var markdown = response.Markdown ?? string.Empty;

        if (request.OutputPath is null)
            return (ConversionResult.Ok(markdown), false);

        try
        {
            var dir = Path.GetDirectoryName(request.OutputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(request.OutputPath, markdown, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Converted {File} but could not write the result", request.SourcePath);
            return (ConversionResult.Fail($"Converted, but the result couldn't be saved: {ex.Message}"), false);
        }

        return (ConversionResult.Ok(markdown, request.OutputPath), false);
    }

    /// <summary>
    /// Nobody wants to see a raw Python traceback, so we boil it down to one line. Python puts the actual
    /// exception on the last line ("module.SomeError: message"), so that's what we reach for.
    /// </summary>
    /// <remarks>
    /// Individual documents no longer reach this: the worker catches its own exceptions and reports a
    /// clean message per file. What still arrives here is stderr from a worker that died outright, which
    /// is precisely when a traceback would otherwise land in front of the user.
    /// </remarks>
    internal static string SummarizeError(string stderr, int exitCode)
    {
        var lines = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
            return $"MarkItDown could not convert the file (exit code {exitCode}).";

        // Python prints the exception on the last line, e.g. "module.SomeException: message".
        var last = lines[^1];
        var colon = last.IndexOf(": ", StringComparison.Ordinal);
        var message = colon >= 0 ? last[(colon + 2)..] : last;

        return $"MarkItDown could not convert the file: {message}";
    }

    private sealed record WorkerResponse(string? Path, bool Ok, string? Markdown, string? Error);

    /// <summary>One running worker process, plus the plumbing needed to talk to it safely.</summary>
    private sealed class Worker : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _stderr = new();

        private Worker(Process process)
        {
            _process = process;

            // stderr has to be drained continuously. MarkItDown prints warnings on import (the missing
            // ffmpeg one, for instance) and a full pipe buffer would block the worker mid-conversion.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await _process.StandardError.ReadLineAsync() is { } line)
                        lock (_stderr)
                        {
                            if (_stderr.Length < 4000) _stderr.AppendLine(line);
                        }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            });
        }

        /// <summary>The tail of anything the worker complained about, for when it dies without answering.</summary>
        public string LastError
        {
            get { lock (_stderr) return _stderr.ToString().Trim(); }
        }

        /// <summary>The worker's exit code, or -1 while it is still running.</summary>
        public int ExitCode
        {
            get
            {
                try { return _process.HasExited ? _process.ExitCode : -1; }
                catch (InvalidOperationException) { return -1; }
            }
        }

        public static Worker Start(string pythonExe, string scriptPath)
        {
            var psi = new ProcessStartInfo(pythonExe, $"\"{scriptPath}\"")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Force UTF-8 so accented and non-ASCII content survives the round trip.
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                Environment = { ["PYTHONIOENCODING"] = "utf-8" }
            };

            var process = Process.Start(psi)
                ?? throw new Core.Exceptions.ConversionException("Failed to start the conversion worker.");

            return new Worker(process);
        }

        /// <summary>
        /// Sends one path and waits for its reply. Returns null when the worker died instead of
        /// answering, and throws <see cref="OperationCanceledException"/> if it went quiet for too long.
        /// </summary>
        public async Task<string?> RequestAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timed.CancelAfter(timeout);

            try
            {
                await _process.StandardInput.WriteLineAsync(path.AsMemory(), timed.Token);
                await _process.StandardInput.FlushAsync(timed.Token);
                return await _process.StandardOutput.ReadLineAsync(timed.Token);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Writing to a dead worker: treat it like any other unexpected death.
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }

            _process.Dispose();
        }
    }
}
