using FluentAssertions;
using MdPipe.Core.Services;

namespace MdPipe.Core.Tests;

public sealed class FormatCatalogProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mdpipe-tests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public FormatCatalogProviderTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "formats.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Get_WithNoCatalogOnDisk_FallsBackToTheBundledList()
    {
        var result = new FormatCatalogProvider(_path).Get();

        result.IsBaseline.Should().BeTrue();
        result.Extensions.Should().Contain([".pdf", ".docx", ".xlsx"]);
    }

    [Fact]
    public void Get_WithACatalogFromTheEngine_UsesIt()
    {
        File.WriteAllText(_path, """
            {"engineVersion":"0.9.9","extensions":[".pdf",".weird"],
             "converters":[{"name":"PdfConverter","extensions":[".pdf"]}]}
            """);

        var result = new FormatCatalogProvider(_path).Get();

        result.IsBaseline.Should().BeFalse();
        result.EngineVersion.Should().Be("0.9.9");
        result.Extensions.Should().Equal(".pdf", ".weird");
        result.Converters.Should().ContainSingle().Which.Name.Should().Be("PdfConverter");
    }

    [Fact]
    public void Get_WhenTheCatalogIsRewritten_PicksUpTheNewOne()
    {
        // Setup writes this file after the app has already started, so a stale read would leave the
        // user looking at the bundled list until the next launch.
        File.WriteAllText(_path, """{"engineVersion":"0.1.7","extensions":[".pdf"],"converters":[]}""");
        var provider = new FormatCatalogProvider(_path);
        provider.Get().Extensions.Should().ContainSingle();

        File.WriteAllText(_path, """{"engineVersion":"0.2.0","extensions":[".pdf",".epub"],"converters":[]}""");
        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddSeconds(1));

        provider.Get().Extensions.Should().HaveCount(2);
        provider.Get().EngineVersion.Should().Be("0.2.0");
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"engineVersion":"0.1.7","extensions":[],"converters":[]}""")]
    public void Get_WithAnUnusableCatalog_FallsBackInsteadOfThrowing(string contents)
    {
        // Refusing to convert because a cache file is malformed would be a terrible trade.
        File.WriteAllText(_path, contents);

        var result = new FormatCatalogProvider(_path).Get();

        result.IsBaseline.Should().BeTrue();
    }
}
