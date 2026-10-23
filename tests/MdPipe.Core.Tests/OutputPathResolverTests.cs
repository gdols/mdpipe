using FluentAssertions;
using MdPipe.Core.Services;

namespace MdPipe.Core.Tests;

public class OutputPathResolverTests
{
    private static string Out(string relative) => Path.GetFullPath(Path.Combine(@"C:\out", relative));

    private static string[] Names(params string[] sources) =>
        OutputPathResolver.Plan(sources, @"C:\out").Select(p => Path.GetFileName(p.FullPath)).ToArray();

    [Fact]
    public void DistinctNames_AreLeftAlone()
    {
        var plan = OutputPathResolver.Plan([@"C:\docs\report.pdf", @"C:\docs\invoice.docx"], @"C:\out");

        plan.Select(p => p.FullPath).Should().Equal(Out("report.md"), Out("invoice.md"));
        plan.Should().OnlyContain(p => !p.Renamed);
    }

    [Fact]
    public void SameNameDifferentExtension_KeepsTheExtension()
    {
        // report.md and report-2.md said nothing about which one came from the PDF.
        var plan = OutputPathResolver.Plan([@"C:\docs\report.pdf", @"C:\docs\report.docx"], null);

        plan.Select(p => p.FullPath).Should().Equal(
            Path.GetFullPath(@"C:\docs\report.pdf.md"), Path.GetFullPath(@"C:\docs\report.docx.md"));
        plan.Should().OnlyContain(p => p.Renamed);
    }

    [Fact]
    public void SameNameSameExtension_FlattenedIntoOneFolder_GetsANumber()
    {
        // The extension can't tell these apart, so a number has to.
        Names(@"C:\docs\2025\report.pdf", @"C:\docs\2026\report.pdf").Should().Equal("report.md", "report-2.md");
    }

    [Fact]
    public void ANameClashThatIsAlsoAnExtensionClash_StillEndsUpUnique()
    {
        Names(@"C:\a\report.pdf", @"C:\b\report.pdf", @"C:\c\report.docx")
            .Should().Equal("report.pdf.md", "report.pdf-2.md", "report.docx.md");
    }

    [Fact]
    public void WithNoOutputFolder_SitsNextToTheOriginal()
    {
        var plan = OutputPathResolver.Plan([@"C:\docs\report.pdf"], null);

        plan[0].FullPath.Should().Be(Path.GetFullPath(@"C:\docs\report.md"));
        plan[0].Renamed.Should().BeFalse();
    }

    [Fact]
    public void SameNamesInTheirOwnFolders_AreNotAClash()
    {
        var plan = OutputPathResolver.Plan([@"C:\docs\2025\report.pdf", @"C:\docs\2026\report.pdf"], null);

        plan.Select(p => p.FullPath).Should().Equal(
            Path.GetFullPath(@"C:\docs\2025\report.md"), Path.GetFullPath(@"C:\docs\2026\report.md"));
        plan.Should().OnlyContain(p => !p.Renamed);
    }

    [Fact]
    public void WhenTheOutputWouldBeTheSourceItself_MovesOutOfTheWay()
    {
        // Converting notes.md next to itself would destroy the original.
        var plan = OutputPathResolver.Plan([@"C:\docs\notes.md"], null);

        plan[0].FullPath.Should().Be(Path.GetFullPath(@"C:\docs\notes-2.md"));
        plan[0].Renamed.Should().BeTrue();
    }

    [Fact]
    public void ClashesAreCaseInsensitive()
    {
        Names(@"C:\a\Report.pdf", @"C:\b\report.pdf").Should().Equal("Report.md", "report-2.md");
    }

    [Fact]
    public void FilesAlreadyOnDisk_AreOverwrittenUnlessAskedToKeepThem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdpipe-outpath", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "notes.md"), "my own notes");
            var source = Path.Combine(dir, "notes.docx");

            OutputPathResolver.Plan([source], null)[0].FullPath
                .Should().Be(Path.Combine(dir, "notes.md"), "re-converting replaces by default");

            var kept = OutputPathResolver.Plan([source], null, keepExisting: true)[0];
            kept.FullPath.Should().Be(Path.Combine(dir, "notes-2.md"));
            kept.Renamed.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
