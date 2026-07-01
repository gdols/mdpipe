using System.Diagnostics;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Infrastructure.Python;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.MarkItDown;

public sealed class MarkItDownConverter(
    PythonEnvironmentManager environmentManager,
    ILogger<MarkItDownConverter> logger) : IMarkItDownConverter
{
    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.SourcePath))
            return ConversionResult.Fail($"Source file not found: {request.SourcePath}");

        var pythonExe = environmentManager.GetPythonExecutable();
        if (pythonExe is null)
            return ConversionResult.Fail("Python environment is not ready. Run 'mdpipe setup' first.");

        logger.LogInformation("Converting {File}", request.SourcePath);

        try
        {
            var markdown = await RunMarkItDownAsync(pythonExe, request.SourcePath, cancellationToken);

            if (request.OutputPath is not null)
            {
                var dir = Path.GetDirectoryName(request.OutputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(request.OutputPath, markdown, cancellationToken);
                return ConversionResult.Ok(markdown, request.OutputPath);
            }

            return ConversionResult.Ok(markdown);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Conversion failed for {File}", request.SourcePath);
            return ConversionResult.Fail(ex.Message);
        }
    }

    private static async Task<string> RunMarkItDownAsync(string pythonExe, string sourcePath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(pythonExe, $"-m markitdown \"{sourcePath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Force UTF-8 so accented/non-ASCII content survives (Spanish, etc.).
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            Environment = { ["PYTHONIOENCODING"] = "utf-8" }
        };

        using var process = Process.Start(psi)
            ?? throw new ConversionException("Failed to start MarkItDown process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await stderrTask;
            throw new ConversionException(SummarizeError(error, process.ExitCode));
        }

        return await stdoutTask;
    }

    /// <summary>
    /// Nobody wants to see a raw Python traceback, so we boil it down to one line. Python puts the actual
    /// exception on the last line ("module.SomeError: message"), so that's what we reach for.
    /// </summary>
    private static string SummarizeError(string stderr, int exitCode)
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
}
