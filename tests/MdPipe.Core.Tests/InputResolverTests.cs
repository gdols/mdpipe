using FluentAssertions;
using MdPipe.Core.Services;

namespace MdPipe.Core.Tests;

public sealed class InputResolverTests : IDisposable
{
    // Pointed at a catalog that doesn't exist on purpose: the tests then run against the bundled
    // baseline instead of whatever MarkItDown happens to be installed on the machine running them.
    private readonly InputResolver _sut = new(new FormatCatalogProvider(
        Path.Combine(Path.GetTempPath(), "mdpipe-tests", "no-catalog-here.json")));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mdpipe-tests", Guid.NewGuid().ToString("N"));

    public InputResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string CreateFile(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return full;
    }

    private static IEnumerable<string> NamesOf(IEnumerable<string> paths) => paths.Select(Path.GetFileName)!;

    [Fact]
    public void Resolve_WithSeveralFiles_KeepsThemInOrder()
    {
        var a = CreateFile("a.pdf");
        var b = CreateFile("b.docx");

        var result = _sut.Resolve([a, b]);

        result.Files.Should().Equal(a, b);
        result.NotFound.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_WithAnExplicitFile_TakesItEvenIfTheExtensionIsUnusual()
    {
        // If you named the file yourself, you meant it.
        var odd = CreateFile("notes.weird");

        var result = _sut.Resolve([odd]);

        result.Files.Should().ContainSingle().Which.Should().Be(odd);
    }

    [Fact]
    public void Resolve_WithAFolder_TakesOnlySupportedFilesAndIgnoresSubfolders()
    {
        CreateFile("docs/report.pdf");
        CreateFile("docs/notes.txt");
        CreateFile("docs/program.exe");
        CreateFile("docs/deep/hidden.pdf");

        var result = _sut.Resolve([Path.Combine(_root, "docs")]);

        NamesOf(result.Files).Should().BeEquivalentTo(["report.pdf", "notes.txt"]);
    }

    [Fact]
    public void Resolve_WithAFolderAndRecursive_IncludesSubfolders()
    {
        CreateFile("docs/report.pdf");
        CreateFile("docs/deep/deeper/hidden.pdf");

        var result = _sut.Resolve([Path.Combine(_root, "docs")], recursive: true);

        NamesOf(result.Files).Should().BeEquivalentTo(["report.pdf", "hidden.pdf"]);
    }

    [Fact]
    public void Resolve_WithAWildcard_ExpandsIt()
    {
        CreateFile("one.pdf");
        CreateFile("two.pdf");
        CreateFile("three.docx");

        var result = _sut.Resolve([Path.Combine(_root, "*.pdf")]);

        NamesOf(result.Files).Should().BeEquivalentTo(["one.pdf", "two.pdf"]);
    }

    [Fact]
    public void Resolve_WithAWildcardAndRecursive_ReachesSubfolders()
    {
        CreateFile("one.pdf");
        CreateFile("deep/two.pdf");

        var result = _sut.Resolve([Path.Combine(_root, "*.pdf")], recursive: true);

        NamesOf(result.Files).Should().BeEquivalentTo(["one.pdf", "two.pdf"]);
    }

    [Fact]
    public void Resolve_WithTheSameFileTwice_KeepsOneCopy()
    {
        var file = CreateFile("docs/report.pdf");

        var result = _sut.Resolve([file, Path.Combine(_root, "docs"), file]);

        result.Files.Should().ContainSingle();
        result.NotFound.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_WithAMissingPath_ReportsItInsteadOfIgnoringIt()
    {
        var real = CreateFile("a.pdf");

        var result = _sut.Resolve([real, Path.Combine(_root, "nope.pdf")]);

        result.Files.Should().ContainSingle().Which.Should().Be(real);
        result.NotFound.Should().ContainSingle().Which.Should().EndWith("nope.pdf");
    }

    [Fact]
    public void Resolve_WithAnEmptyFolder_ReportsIt()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        var result = _sut.Resolve([empty]);

        result.Files.Should().BeEmpty();
        result.NotFound.Should().ContainSingle().Which.Should().Be(empty);
    }

    [Fact]
    public void Resolve_WithAWildcardThatMatchesNothing_ReportsIt()
    {
        CreateFile("a.pdf");

        var result = _sut.Resolve([Path.Combine(_root, "*.xlsx")]);

        result.Files.Should().BeEmpty();
        result.NotFound.Should().ContainSingle();
    }

    [Fact]
    public void Resolve_WithQuotedOrPaddedInput_StillFindsTheFile()
    {
        var file = CreateFile("a.pdf");

        var result = _sut.Resolve([$"  \"{file}\"  "]);

        result.Files.Should().ContainSingle().Which.Should().Be(file);
    }

    [Fact]
    public void Resolve_WithNoInput_ReturnsNothing()
    {
        var result = _sut.Resolve([]);

        result.Files.Should().BeEmpty();
        result.NotFound.Should().BeEmpty();
    }

    [Theory]
    [InlineData("legacy.doc")]   // no converter in MarkItDown, used to fail at conversion time
    [InlineData("legacy.ppt")]   // same
    [InlineData("notes.md")]     // would convert onto itself and rewrite the user's own file
    [InlineData("program.exe")]
    public void Resolve_WithAFolder_LeavesOutWhatWeShouldNotConvert(string fileName)
    {
        CreateFile("docs/keep.pdf");
        CreateFile("docs/" + fileName);

        var result = _sut.Resolve([Path.Combine(_root, "docs")]);

        NamesOf(result.Files).Should().BeEquivalentTo(["keep.pdf"]);
    }

    [Theory]
    [InlineData("book.epub")]
    [InlineData("bundle.zip")]
    [InlineData("mail.msg")]
    [InlineData("sheet.xls")]
    [InlineData("notebook.ipynb")]
    [InlineData("audio.mp3")]
    public void Resolve_WithAFolder_PicksUpEverythingMarkItDownConverts(string fileName)
    {
        CreateFile("docs/" + fileName);

        var result = _sut.Resolve([Path.Combine(_root, "docs")]);

        NamesOf(result.Files).Should().BeEquivalentTo([fileName]);
    }

    [Fact]
    public void Resolve_WithAnExplicitFile_StillTakesFormatsAFolderScanWouldSkip()
    {
        // Naming a file yourself is a deliberate act, so the filter doesn't apply.
        var legacy = CreateFile("legacy.doc");

        var result = _sut.Resolve([legacy]);

        result.Files.Should().ContainSingle().Which.Should().Be(legacy);
    }

    [Fact]
    public void Resolve_WithARecursiveWildcard_WalksTheTreeWithoutThrowing()
    {
        // Regression: this used to go through Directory.EnumerateFiles(AllDirectories), whose lazy
        // enumeration threw past the try/catch and took the whole command down.
        CreateFile("one.pdf");
        CreateFile("deep/two.pdf");
        CreateFile("deep/deeper/three.pdf");

        var act = () => _sut.Resolve([Path.Combine(_root, "*.pdf")], recursive: true);

        act.Should().NotThrow();
        NamesOf(act().Files).Should().BeEquivalentTo(["one.pdf", "two.pdf", "three.pdf"]);
    }

    [Fact]
    public void Resolve_WithIncludeEverything_TakesFilesTheFilterWouldSkip()
    {
        // The escape hatch for files whose extension lies or is missing: the engine decides by content.
        CreateFile("docs/report.pdf");
        CreateFile("docs/no_extension_at_all");
        CreateFile("docs/mislabelled.zzz");

        var result = _sut.Resolve([Path.Combine(_root, "docs")], includeEverything: true);

        NamesOf(result.Files).Should().BeEquivalentTo(["report.pdf", "no_extension_at_all", "mislabelled.zzz"]);
    }

    [Fact]
    public void Resolve_WithIncludeEverything_StillLeavesMarkdownAlone()
    {
        // Even in "try everything" mode, converting a .md would write over the file it just read.
        CreateFile("docs/report.pdf");
        CreateFile("docs/notes.md");

        var result = _sut.Resolve([Path.Combine(_root, "docs")], includeEverything: true);

        NamesOf(result.Files).Should().BeEquivalentTo(["report.pdf"]);
    }

    [Fact]
    public void Resolve_OnAReadableTree_ReportsNothingAsUnreadable()
    {
        CreateFile("docs/a.pdf");

        var result = _sut.Resolve([Path.Combine(_root, "docs")], recursive: true);

        result.Unreadable.Should().BeEmpty();
    }
}
