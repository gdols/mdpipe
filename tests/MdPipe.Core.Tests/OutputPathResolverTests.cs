using FluentAssertions;
using MdPipe.Core.Services;

namespace MdPipe.Core.Tests;

public class OutputPathResolverTests
{
    private readonly OutputPathResolver _sut = new();

    private static string Out(string relative) => Path.GetFullPath(Path.Combine(@"C:\out", relative));

    [Fact]
    public void For_WithDistinctNames_LeavesThemAlone()
    {
        var first = _sut.For(@"C:\docs\report.pdf", @"C:\out");
        var second = _sut.For(@"C:\docs\invoice.docx", @"C:\out");

        first.FullPath.Should().Be(Out("report.md"));
        second.FullPath.Should().Be(Out("invoice.md"));
        first.Renamed.Should().BeFalse();
        second.Renamed.Should().BeFalse();
    }

    [Fact]
    public void For_WhenTwoSourcesFlattenToTheSameName_SuffixesTheSecond()
    {
        var first = _sut.For(@"C:\docs\2025\report.pdf", @"C:\out");
        var second = _sut.For(@"C:\docs\2026\report.pdf", @"C:\out");

        first.FullPath.Should().Be(Out("report.md"));
        second.FullPath.Should().Be(Out("report-2.md"));
        first.Renamed.Should().BeFalse();
        second.Renamed.Should().BeTrue();
    }

    [Fact]
    public void For_WithSeveralClashes_KeepsCounting()
    {
        _sut.For(@"C:\a\report.pdf", @"C:\out");
        _sut.For(@"C:\b\report.docx", @"C:\out");
        var third = _sut.For(@"C:\c\report.pptx", @"C:\out");

        third.FullPath.Should().Be(Out("report-3.md"));
        third.Renamed.Should().BeTrue();
    }

    [Fact]
    public void For_WithNoOutputFolder_SitsNextToTheOriginal()
    {
        var result = _sut.For(@"C:\docs\report.pdf", null);

        result.FullPath.Should().Be(Path.GetFullPath(@"C:\docs\report.md"));
        result.Renamed.Should().BeFalse();
    }

    [Fact]
    public void For_WithSameNamesInTheirOwnFolders_DoesNotTreatThemAsAClash()
    {
        var first = _sut.For(@"C:\docs\2025\report.pdf", null);
        var second = _sut.For(@"C:\docs\2026\report.pdf", null);

        first.FullPath.Should().Be(Path.GetFullPath(@"C:\docs\2025\report.md"));
        second.FullPath.Should().Be(Path.GetFullPath(@"C:\docs\2026\report.md"));
        second.Renamed.Should().BeFalse();
    }

    [Fact]
    public void For_WhenTheOutputWouldBeTheSourceItself_MovesOutOfTheWay()
    {
        // Converting notes.md next to itself would destroy the original.
        var result = _sut.For(@"C:\docs\notes.md", null);

        result.FullPath.Should().Be(Path.GetFullPath(@"C:\docs\notes-2.md"));
        result.Renamed.Should().BeTrue();
    }

    [Fact]
    public void For_IsCaseInsensitiveAboutClashes()
    {
        _sut.For(@"C:\a\Report.pdf", @"C:\out");
        var second = _sut.For(@"C:\b\report.pdf", @"C:\out");

        second.Renamed.Should().BeTrue();
    }
}
