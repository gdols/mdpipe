namespace MdPipe.Core.Models;

public sealed class ConversionRequest
{
    public required string SourcePath { get; init; }
    public string? OutputPath { get; init; }

    public static ConversionRequest FromFile(string sourcePath, string? outputPath = null) =>
        new() { SourcePath = sourcePath, OutputPath = outputPath };
}
