using FluentAssertions;
using MdPipe.Core.Exceptions;
using MdPipe.Infrastructure.Manifest;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// The manifest is read by every copy of MdPipe ever downloaded, including builds that predate any
/// field added to it. These cover both directions of that.
/// </summary>
public sealed class ManifestSchemaTests
{
    private const string SchemaOne = """
        {
          "schemaVersion": 1,
          "stableVersion": "0.1.7",
          "minimumVersion": "0.1.5",
          "compatibleVersions": ["0.1.5", "0.1.6", "0.1.7"],
          "updatedAt": "2026-08-05",
          "notes": "whatever"
        }
        """;

    private const string SchemaTwo = """
        {
          "schemaVersion": 2,
          "stableVersion": "0.1.7",
          "minimumVersion": "0.1.5",
          "compatibleVersions": ["0.1.5", "0.1.6", "0.1.7"],
          "updatedAt": "2026-08-05",
          "notes": "whatever",
          "app": {
            "latestVersion": "0.5.0",
            "releaseUrl": "https://github.com/gdols/MdPipe/releases/latest",
            "criticalBelow": "0.3.0",
            "notes": "Fixes the folder scan freezing."
          }
        }
        """;

    [Fact]
    public void TheOlderSchemaStillReads()
    {
        var manifest = ManifestSerializer.Deserialize(SchemaOne);

        manifest.StableVersion.Should().Be("0.1.7");
        manifest.App.Should().BeNull();
    }

    [Fact]
    public void TheNewerSchemaCarriesTheReleaseBlock()
    {
        var manifest = ManifestSerializer.Deserialize(SchemaTwo);

        manifest.App.Should().NotBeNull();
        manifest.App!.LatestVersion.Should().Be("0.5.0");
        manifest.App.CriticalBelow.Should().Be("0.3.0");
        manifest.App.Notes.Should().Be("Fixes the folder scan freezing.");
    }

    [Fact]
    public void FieldsAnOlderBuildDoesNotKnowAboutAreIgnored()
    {
        // This is the whole reason the release block travels inside the compatibility manifest: every
        // executable already downloaded goes on reading the file after the schema grows.
        var withFutureFields = SchemaTwo.Replace(
            "\"schemaVersion\": 2,",
            "\"schemaVersion\": 3,\n  \"somethingInvented\": {\"deeply\": [\"nested\", 1, true]},");

        var manifest = ManifestSerializer.Deserialize(withFutureFields);

        manifest.StableVersion.Should().Be("0.1.7");
        manifest.CompatibleVersions.Should().HaveCount(3);
    }

    [Fact]
    public void AHalfWrittenReleaseBlockLeavesTheRestUsable()
    {
        // The version gate matters more than an update notice, so a broken app block is dropped
        // rather than allowed to take the file down with it.
        var manifest = ManifestSerializer.Deserialize(
            SchemaTwo.Replace("\"latestVersion\": \"0.5.0\",", "\"latestVersion\": \"\","));

        manifest.App.Should().BeNull();
        manifest.StableVersion.Should().Be("0.1.7");
    }

    [Fact]
    public async Task TheManifestShippedInThisRepositoryParses()
    {
        // Catches a typo in the real file before it reaches every installed copy at once.
        var manifest = await new EmbeddedManifestProvider().GetManifestAsync();

        manifest.CompatibleVersions.Should().Contain(manifest.StableVersion);
        manifest.App.Should().NotBeNull("the repository manifest is on schema 2");
        manifest.App!.ReleaseUrl.Should().StartWith("https://github.com/gdols/MdPipe/releases");
    }

    [Fact]
    public void RubbishIsStillRejected()
    {
        var act = () => ManifestSerializer.Deserialize("{ not json at all");

        act.Should().Throw<ManifestException>();
    }
}
