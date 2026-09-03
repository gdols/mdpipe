using FluentAssertions;
using MdPipe.Infrastructure.Python;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// Whether a reply from the worker is a usable list of formats, or a discovery that has broken.
/// </summary>
/// <remarks>
/// MarkItDown offers no public way to enumerate its converters, so MdPipe reaches for a private
/// attribute and reads module constants whose names are not even consistent between converters. Any
/// release can break that without breaking anything else.
/// <para>
/// The failure to avoid is the quiet one: writing an empty catalogue over a good one drops the app
/// back to the list compiled into the build, and the window then confidently reports formats this
/// machine may not have. An engine that reads nothing at all is not a real state, so a reply that
/// claims it is treated as a broken answer rather than an honest zero.
/// </para>
/// </remarks>
public sealed class FormatDiscoveryTests
{
    [Fact]
    public void ARealCatalogueIsAccepted()
    {
        var json = """{"engineVersion":"0.1.7","extensions":[".pdf",".docx"],"converters":[]}""";

        PythonEnvironmentManager.DescribesFormats(json, out var complaint).Should().BeTrue();
        complaint.Should().BeEmpty();
    }

    [Fact]
    public void AnErrorFromTheWorkerIsPassedOnRatherThanStored()
    {
        var json = """{"error":"This MarkItDown does not expose its converters the way MdPipe reads them"}""";

        PythonEnvironmentManager.DescribesFormats(json, out var complaint).Should().BeFalse();
        complaint.Should().Contain("does not expose its converters");
    }

    [Fact]
    public void AnEmptyListIsRefused()
    {
        // The shape a restructured MarkItDown would produce: the private list is still there, and
        // nothing recognisable comes out of it.
        var json = """{"engineVersion":"0.2.0","extensions":[],"converters":[]}""";

        PythonEnvironmentManager.DescribesFormats(json, out var complaint).Should().BeFalse();
        complaint.Should().Contain("no formats");
    }

    [Fact]
    public void AReplyWithNoExtensionsFieldIsRefused()
    {
        var json = """{"engineVersion":"0.2.0"}""";

        PythonEnvironmentManager.DescribesFormats(json, out _).Should().BeFalse();
    }

    [Fact]
    public void RubbishIsRefusedWithTheReason()
    {
        PythonEnvironmentManager.DescribesFormats("not json at all", out var complaint).Should().BeFalse();
        complaint.Should().Contain("could not be read");
    }

    [Fact]
    public void AnErrorWinsOverAnythingElseInTheReply()
    {
        // Belt and braces: if the worker ever reports both, the complaint is the part that matters.
        var json = """{"error":"discovery is broken","extensions":[".pdf"]}""";

        PythonEnvironmentManager.DescribesFormats(json, out var complaint).Should().BeFalse();
        complaint.Should().Be("discovery is broken");
    }
}
