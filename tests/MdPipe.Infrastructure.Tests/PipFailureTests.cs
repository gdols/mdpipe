using FluentAssertions;
using MdPipe.Infrastructure.Python;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// Telling pip's two failures apart: it could not reach anything, or nothing it reached would fit.
/// </summary>
/// <remarks>
/// They look identical in a dialog box and lead somewhere completely different. Someone reported a
/// first run that failed, and the message sent them to check their proxy, firewall and antivirus
/// when the real cause was the Python they had installed. The reflex is to blame the network, and
/// the message was feeding it.
/// </remarks>
public sealed class PipFailureTests
{
    /// <summary>
    /// What pip actually printed on that machine, trimmed but otherwise as it arrived. Python 3.14
    /// was installed; every version of the dependency MarkItDown pins declares support for 3.13 and
    /// below, so there was nothing to install.
    /// </summary>
    private const string TheRealOne = """
        ERROR: Ignored the following versions that require a different python version: 0.5.0
        Requires-Python >=3.8,<3.12; 0.5.1 Requires-Python >=3.8,<3.13; 1.0.3 Requires-Python
        >=3.8,<3.14; 1.2.2 Requires-Python >=3.8,<3.14
        ERROR: Could not find a version that satisfies the requirement youtube-transcript-api~=1.0.0;
        extra == "all" (from markitdown[all])
        ERROR: No matching distribution found for youtube-transcript-api~=1.0.0; extra == "all"
        """;

    [Fact]
    public void TheReportedFailureIsRecognisedForWhatItWas()
    {
        PythonEnvironmentManager.LooksLikeAVersionClash(TheRealOne).Should().BeTrue();
    }

    [Theory]
    [InlineData("ERROR: Could not find a version that satisfies the requirement foo")]
    [InlineData("ERROR: No matching distribution found for bar")]
    [InlineData("ERROR: Ignored the following versions that require a different python version: 1.0")]
    public void EachOfPipsWaysOfSayingItIsRecognised(string output)
    {
        PythonEnvironmentManager.LooksLikeAVersionClash(output).Should().BeTrue();
    }

    [Theory]
    [InlineData("WARNING: Retrying after connection broken by 'NewConnectionError'")]
    [InlineData("ERROR: Could not install packages due to an OSError: [Errno 13] Permission denied")]
    [InlineData("SSLError(SSLCertVerificationError(1, 'certificate verify failed'))")]
    [InlineData("ERROR: Operation cancelled by user")]
    [InlineData("")]
    public void ARealNetworkOrDiskFailureIsNotMistakenForOne(string output)
    {
        // The other direction matters just as much. Claiming a proxy problem is a Python problem
        // would send the next person looking in the wrong place, which is the bug being fixed.
        PythonEnvironmentManager.LooksLikeAVersionClash(output).Should().BeFalse();
    }

    [Fact]
    public void TheMatchDoesNotCareAboutCase()
    {
        PythonEnvironmentManager.LooksLikeAVersionClash("no matching DISTRIBUTION found for x")
            .Should().BeTrue();
    }
}
