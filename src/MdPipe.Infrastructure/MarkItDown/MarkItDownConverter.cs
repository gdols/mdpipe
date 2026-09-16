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
/// Importing MarkItDown costs about two seconds and converting a small document a tenth of that, so
/// the worker starts once per batch and is fed one path at a time. If the interpreter dies, the file
/// in flight is failed and the worker restarted for the rest, so one bad document can't end a batch.
/// </remarks>
public sealed class MarkItDownConverter(
    IConversionWorkerSource workerSource,
    ILogger<MarkItDownConverter> logger,
    TimeSpan? perFileTimeout = null) : IMarkItDownConverter
{
    /// <summary>
    /// Deliberately generous: a large document can legitimately take minutes, so this is a "something
    /// is stuck" threshold, not a performance budget. Overridable so its test doesn't take five.
    /// </summary>
    private readonly TimeSpan _perFileTimeout = perFileTimeout ?? TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// No byte order mark, which matters on the way in: a BOM would be written once at the top of the
    /// pipe and read as part of the first path.
    /// </summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async IAsyncEnumerable<ConversionResult> ConvertManyAsync(
        IReadOnlyList<ConversionRequest> requests,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pythonExe = workerSource.PythonExecutable;
        if (pythonExe is null)
        {
            foreach (var _ in requests)
                yield return ConversionResult.Fail("Python environment is not ready. Run 'mdpipe setup' first.");
            yield break;
        }

        var script = workerSource.EnsureWorkerScript();
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
                    // The interpreter went down with the file. Start clean for the rest of the batch.
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
            line = await worker.RequestAsync(request.SourcePath, _perFileTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Conversion of {File} timed out after {Minutes} minutes", request.SourcePath, _perFileTimeout.TotalMinutes);
            return (ConversionResult.Fail($"Timed out after {_perFileTimeout.TotalMinutes:0} minutes."), true);
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
            return (ConversionResult.Fail(
                LegacyOfficeAdvice(request.SourcePath) ?? $"MarkItDown could not convert the file: {reason}"), false);
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
    /// Boils a Python traceback down to one line. Python puts the real exception last
    /// ("module.SomeError: message"), so that's what we reach for. Only reached when a worker died
    /// outright, which is precisely when a traceback would otherwise land in front of the user.
    /// </summary>
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

    /// <summary>
    /// MarkItDown has no converter for the old binary Office formats, and the only real fix is on the
    /// user's side. "No converter attempted a conversion" tells them nothing; "save it as .docx"
    /// tells them everything.
    /// </summary>
    private static string? LegacyOfficeAdvice(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".doc" => "Word 97-2003 files (.doc) aren't supported. Open it in Word and save it as .docx, then convert that.",
            ".ppt" => "PowerPoint 97-2003 files (.ppt) aren't supported. Open it in PowerPoint and save it as .pptx, then convert that.",
            ".xlw" => "This is a Excel 4.0 workspace, which isn't supported. Open it in Excel and save it as .xlsx, then convert that.",
            _ => null
        };

    private sealed record WorkerResponse(string? Path, bool Ok, string? Markdown, string? Error);

    /// <summary>One running worker process, and the plumbing to talk to it safely.</summary>
    private sealed class Worker : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _stderr = new();

        private Worker(Process process)
        {
            _process = process;

            // stderr must be drained continuously: MarkItDown prints warnings on import (the
            // missing ffmpeg one) and a full pipe buffer would block the worker mid-conversion.
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

        /// <summary>What the worker complained about, for when it dies without answering.</summary>
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
                // UTF-8 in all three directions. Input is the one that gets forgotten: without it
                // .NET writes in the console code page while the worker reads UTF-8, so any path
                // with an accent in it arrives as bytes Python refuses to decode.
                StandardInputEncoding = Utf8NoBom,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
                Environment = { ["PYTHONIOENCODING"] = "utf-8" }
            };

            var process = Process.Start(psi)
                ?? throw new Core.Exceptions.ConversionException("Failed to start the conversion worker.");

            return new Worker(process);
        }

        /// <summary>
        /// Sends one path and waits for the reply. Null when the worker died instead of answering;
        /// throws <see cref="OperationCanceledException"/> if it went quiet for too long.
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
                return null;   // Writing to a dead worker is just another way of it dying.
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
