using FluentAssertions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Infrastructure.MarkItDown;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Infrastructure.Tests;

/// <summary>
/// What the converter does when the worker on the other end of the pipe misbehaves: dies halfway,
/// goes quiet, answers with rubbish. The difference between a batch of forty documents finishing
/// and one stopping at number three.
/// </summary>
public sealed class MarkItDownConverterBatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mdpipe-converter-tests", Guid.NewGuid().ToString("N"));

    public MarkItDownConverterBatchTests()
    {
        Directory.CreateDirectory(_dir);
        _ = Python.Required;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    private string Document(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "pretend this is a document");
        return path;
    }

    private static MarkItDownConverter Converter(IConversionWorkerSource worker, TimeSpan? timeout = null) =>
        new(worker, NullLogger<MarkItDownConverter>.Instance, timeout);

    private static async Task<List<ConversionResult>> RunAsync(
        MarkItDownConverter converter, IReadOnlyList<ConversionRequest> requests, CancellationToken token = default)
    {
        var results = new List<ConversionResult>();
        await foreach (var result in converter.ConvertManyAsync(requests, token)) results.Add(result);
        return results;
    }

    [Fact]
    public async Task AWholeBatchGoesThroughOneWorker()
    {
        // The reason the worker exists at all. Importing MarkItDown costs about two seconds, so a
        // process per file would put that on every single document.
        using var worker = FakeWorker.WellBehaved(_dir);
        var requests = new[] { "a.pdf", "b.docx", "c.xlsx" }
            .Select(n => ConversionRequest.FromFile(Document(n), null)).ToList();

        var results = await RunAsync(Converter(worker), requests);

        results.Should().HaveCount(3).And.OnlyContain(r => r.Success);
        results.Select(r => r.MarkdownContent).Should().Equal("# a.pdf", "# b.docx", "# c.xlsx");
    }

    [Fact]
    public async Task WhenTheWorkerDiesMidBatch_TheFileInFlightFailsAndTheRestStillConvert()
    {
        // A converter crashing used to cost only its own file, because every file had its own
        // process. Sharing one process gave that away, and restarting is how it was bought back.
        using var worker = FakeWorker.DyingOn(_dir, "poison.pdf");
        var requests = new[] { "first.pdf", "poison.pdf", "third.pdf" }
            .Select(n => ConversionRequest.FromFile(Document(n), null)).ToList();

        var results = await RunAsync(Converter(worker), requests);

        results.Should().HaveCount(3);
        results[0].Success.Should().BeTrue();
        results[1].Success.Should().BeFalse("the worker went down with that file");
        results[2].Success.Should().BeTrue("a restarted worker handles the rest");
        results[2].MarkdownContent.Should().Be("# third.pdf");
    }

    [Fact]
    public async Task WhenTheWorkerDies_ItsComplaintReachesTheUser()
    {
        // Rather than a bare exit code. What the process printed on the way out is usually the only
        // clue there is.
        using var worker = FakeWorker.DyingOn(_dir, "poison.pdf");
        var requests = new[] { "poison.pdf" }.Select(n => ConversionRequest.FromFile(Document(n), null)).ToList();

        var results = await RunAsync(Converter(worker), requests);

        results[0].ErrorMessage.Should().Contain("segfault");
    }

    [Fact]
    public async Task AFileThatGoesQuiet_IsGivenUpOnAndTheRestStillConvert()
    {
        // Without a timeout a single pathological document hangs the batch forever, and in the CLI
        // there is no way out of that at all.
        using var worker = FakeWorker.HangingOn(_dir, "slow.pdf");
        var requests = new[] { "quick.pdf", "slow.pdf", "after.pdf" }
            .Select(n => ConversionRequest.FromFile(Document(n), null)).ToList();

        var results = await RunAsync(Converter(worker, TimeSpan.FromSeconds(3)), requests);

        results.Should().HaveCount(3);
        results[0].Success.Should().BeTrue();
        results[1].Success.Should().BeFalse();
        results[1].ErrorMessage.Should().Contain("Timed out");
        results[2].Success.Should().BeTrue("the stuck worker is replaced rather than waited on again");
    }

    [Fact]
    public async Task AnUnreadableReply_FailsThatFileWithoutLosingTheBatch()
    {
        using var worker = FakeWorker.Babbling(_dir);
        var requests = new[] { "a.pdf", "b.pdf" }
            .Select(n => ConversionRequest.FromFile(Document(n), null)).ToList();

        var results = await RunAsync(Converter(worker), requests);

        results.Should().HaveCount(2).And.OnlyContain(r => !r.Success);
        results[0].ErrorMessage.Should().Contain("unreadable");
    }

    [Fact]
    public async Task AFileTheWorkerRejects_FailsWithTheReasonItGave()
    {
        using var worker = FakeWorker.Refusing(_dir, "File is not a zip file");
        var requests = new[] { "broken.xlsx", "next.pdf" }
            .Select(n => ConversionRequest.FromFile(Document(n), null)).ToList();

        var results = await RunAsync(Converter(worker), requests);

        results[0].Success.Should().BeFalse();
        results[0].ErrorMessage.Should().Contain("not a zip file");
        results[1].Success.Should().BeFalse("this worker refuses everything");
    }

    [Fact]
    public async Task TheMarkdownIsWrittenWhereItWasAskedFor()
    {
        using var worker = FakeWorker.Returning(_dir, "# Título con acentos");
        var output = Path.Combine(_dir, "out", "report.md");
        var requests = new[] { ConversionRequest.FromFile(Document("report.pdf"), output) };

        var results = await RunAsync(Converter(worker), requests);

        results[0].Success.Should().BeTrue();
        results[0].OutputPath.Should().Be(output);
        File.Exists(output).Should().BeTrue("the folder is created if it isn't there");
        (await File.ReadAllTextAsync(output)).Should().Be("# Título con acentos",
            "accents have to survive the pipe, which is why the worker forces UTF-8");
    }

    [Fact]
    public async Task APathWithAccentsInIt_ReachesTheWorkerIntact()
    {
        // Invisible until now because every test here used ASCII names. .NET wrote to the worker
        // in the console code page while the worker read UTF-8, so the path arrived as bytes Python
        // would not decode and the interpreter died in the stdin loop, where nothing catches it.
        // A user account called "Muñoz" was enough to make every conversion fail.
        var name = "informe anual ñ á é í ó ú ü.pdf";
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "pretend this is a document");

        using var worker = FakeWorker.WellBehaved(_dir);   // echoes back the name it was handed

        var results = await RunAsync(Converter(worker), [ConversionRequest.FromFile(path, null)]);

        results[0].Success.Should().BeTrue();
        results[0].MarkdownContent.Should().Be("# " + name, "the worker has to see the name we sent");
    }

    [Fact]
    public async Task AnAccentedFolderIsAlsoFineOnTheWayOut()
    {
        // The output path never crosses the pipe, but worth pinning all the same.
        using var worker = FakeWorker.Returning(_dir, "# hecho");
        var output = Path.Combine(_dir, "Año 2026", "Nómina señor Muñoz.md");

        var results = await RunAsync(
            Converter(worker), [ConversionRequest.FromFile(Document("a.pdf"), output)]);

        results[0].Success.Should().BeTrue();
        File.Exists(output).Should().BeTrue();
        (await File.ReadAllTextAsync(output)).Should().Be("# hecho");
    }

    [Fact]
    public async Task WhenTheResultCannotBeSaved_ItSaysSoRatherThanClaimingSuccess()
    {
        // A folder where a file should be. Converting worked and saving did not, and reporting that
        // as a success would lose the document silently.
        using var worker = FakeWorker.WellBehaved(_dir);
        var blocked = Path.Combine(_dir, "in-the-way.md");
        Directory.CreateDirectory(blocked);

        var results = await RunAsync(
            Converter(worker), new[] { ConversionRequest.FromFile(Document("a.pdf"), blocked) });

        results[0].Success.Should().BeFalse();
        results[0].ErrorMessage.Should().Contain("couldn't be saved");
    }

    [Fact]
    public async Task AMissingSourceFile_IsReportedAndTheRestStillConvert()
    {
        using var worker = FakeWorker.WellBehaved(_dir);
        var requests = new[]
        {
            ConversionRequest.FromFile(Path.Combine(_dir, "never-existed.pdf"), null),
            ConversionRequest.FromFile(Document("real.pdf"), null)
        };

        var results = await RunAsync(Converter(worker), requests);

        results[0].Success.Should().BeFalse();
        results[0].ErrorMessage.Should().Contain("not found");
        results[1].Success.Should().BeTrue();
    }

    [Fact]
    public async Task CancellingStopsTheBatchWhereItIs()
    {
        using var worker = FakeWorker.WellBehaved(_dir);
        using var cancellation = new CancellationTokenSource();
        var requests = Enumerable.Range(0, 20)
            .Select(i => ConversionRequest.FromFile(Document($"f{i}.pdf"), null)).ToList();

        var results = new List<ConversionResult>();
        var act = async () =>
        {
            await foreach (var result in Converter(worker).ConvertManyAsync(requests, cancellation.Token))
            {
                results.Add(result);
                if (results.Count == 3) await cancellation.CancelAsync();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        results.Should().HaveCount(3, "the rest were never started");
    }

    [Fact]
    public async Task WithNoEnvironment_EveryFileIsFailedRatherThanThrowing()
    {
        // The CLI reaches here when nothing has been set up yet, and one clear failure per file is
        // more use than an exception out of the middle of a batch.
        var worker = new NoEnvironment();
        var requests = new[] { "a.pdf", "b.pdf" }
            .Select(n => ConversionRequest.FromFile(Document(n), null)).ToList();

        var results = await RunAsync(Converter(worker), requests);

        results.Should().HaveCount(2).And.OnlyContain(r => !r.Success);
        results[0].ErrorMessage.Should().Contain("setup");
    }

    /// <summary>An environment that was never set up, which is what the CLI meets on a fresh machine.</summary>
    private sealed class NoEnvironment : IConversionWorkerSource
    {
        public string? PythonExecutable => null;
        public string EnsureWorkerScript() => throw new InvalidOperationException("should not be reached");
    }
}
