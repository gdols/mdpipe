namespace MdPipe.Core.Models;

public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string? MarkdownContent { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }

    public static ConversionResult Ok(string markdown, string? outputPath = null) =>
        new() { Success = true, MarkdownContent = markdown, OutputPath = outputPath };

    public static ConversionResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}
