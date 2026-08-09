using FluentAssertions;
using MdPipe.Infrastructure.MarkItDown;

namespace MdPipe.Infrastructure.Tests;

public class MarkItDownConverterTests
{
    [Fact]
    public void SummarizeError_WhenStderrIsEmpty_ReturnsFallbackMessage()
    {
        const string stderr = "";
        const int exitCode = 1;

        var result = MarkItDownConverter.SummarizeError(stderr, exitCode);

        result.Should().Be(
            "MarkItDown could not convert the file (exit code 1).");
    }
    [Fact]
    public void SummarizeError_WhenStderrContainsTraceback_UsesLastLine()
    {
        const string stderr =
            "Traceback (most recent call last):\n" +
            "  File \"convert.py\", line 10, in <module>\n" +
            "PermissionError: Access denied";

        var result = MarkItDownConverter.SummarizeError(stderr, 1);

        result.Should().Be(
            "MarkItDown could not convert the file: Access denied");
    }
    [Fact]
    public void SummarizeError_WhenLastLineHasNoColon_UsesItVerbatim()
    {
        const string stderr =
            "Traceback (most recent call last):\n" +
            "Conversion failed";

        var result = MarkItDownConverter.SummarizeError(stderr, 1);

        result.Should().Be(
            "MarkItDown could not convert the file: Conversion failed");
    }
    [Fact]
    public void SummarizeError_WhenMessageContainsWindowsPath_CutsAtFirstColon()
    {
        const string stderr =
            "PermissionError: [Errno 13] Permission denied: 'C:\\docs\\a.pdf'";

        var result = MarkItDownConverter.SummarizeError(stderr, 1);

        result.Should().Be(
            "MarkItDown could not convert the file: [Errno 13] Permission denied: 'C:\\docs\\a.pdf'");
    }
    [Fact]
    public void SummarizeError_WhenStderrIsWhitespaceOnly_ReturnsFallbackMessage()
    {
        const string stderr = "   \r\n\t  \r\n";

        var result = MarkItDownConverter.SummarizeError(stderr, 2);

        result.Should().Be(
            "MarkItDown could not convert the file (exit code 2).");
    }

    [Fact]
    public void SummarizeError_WhenStderrUsesCrLfAndTrailingBlankLines_UsesLastNonEmptyLine()
    {
        const string stderr =
            "Traceback (most recent call last):\r\n" +
            "  File \"convert.py\", line 10\r\n" +
            "ValueError: Bad input\r\n" +
            "\r\n";

        var result = MarkItDownConverter.SummarizeError(stderr, 1);

        result.Should().Be(
            "MarkItDown could not convert the file: Bad input");
    }
}
