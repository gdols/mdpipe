using System.CommandLine;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;

namespace MdPipe.Cli.Commands;

public static class ConvertCommand
{
    public static Command Build(
        IMarkItDownConverter converter,
        IPythonEnvironmentManager environmentManager,
        IManifestProvider manifestProvider,
        VersionGateService versionGate,
        InputResolver inputResolver)
    {
        var inputsArg = new Argument<string[]>("input")
        {
            Description = "Files, folders or patterns to convert (e.g. report.pdf, .\\docs, *.docx)",
            Arity = ArgumentArity.OneOrMore
        };
        var outputOpt = new Option<string?>("--output", new string[] { "-o" })
        {
            Description = "Output .md file for a single input, or a folder for several. " +
                          "Defaults to stdout for one file, or next to each original."
        };
        var recursiveOpt = new Option<bool>("--recursive", new string[] { "-r" })
        {
            Description = "Look inside subfolders too"
        };

        var command = new Command("convert", "Convert documents to Markdown")
        {
            inputsArg,
            outputOpt,
            recursiveOpt
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var inputs = parseResult.GetValue(inputsArg) ?? [];
            var output = parseResult.GetValue(outputOpt);
            var recursive = parseResult.GetValue(recursiveOpt);

            var resolution = inputResolver.Resolve(inputs, recursive);

            foreach (var missing in resolution.NotFound)
                Console.Error.WriteLine($"Nothing to convert at: {missing}");

            foreach (var blocked in resolution.Unreadable)
                Console.Error.WriteLine($"Skipped (no permission to read): {blocked}");

            if (resolution.Files.Count == 0)
                return 1;

            // Check the environment and the version gate once for the whole batch, not per file.
            try
            {
                var manifest = await manifestProvider.GetManifestAsync(cancellationToken);
                var envInfo = await environmentManager.GetEnvironmentInfoAsync(cancellationToken);

                if (!envInfo.IsReady)
                {
                    Console.Error.WriteLine($"Error: {envInfo.MissingReason}");
                    return 1;
                }

                versionGate.ThrowIfIncompatible(envInfo.InstalledMarkItDownVersion!, manifest);
            }
            catch (VersionGateException ex)
            {
                Console.Error.WriteLine($"Version gate blocked: {ex.Message}");
                return 1;
            }
            catch (ManifestException ex)
            {
                Console.Error.WriteLine($"Warning: Could not verify manifest ({ex.Message}). Proceeding with installed version.");
            }

            // Writing to stdout only makes sense for a single document.
            var toStdout = resolution.Files.Count == 1 && output is null;
            var outputFolder = ResolveOutputFolder(output, inputs, resolution.Files.Count);
            if (outputFolder is not null) Directory.CreateDirectory(outputFolder);

            var converted = 0;
            var failed = 0;

            foreach (var file in resolution.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var outputPath = toStdout ? null : BuildOutputPath(file, output, outputFolder);
                var result = await converter.ConvertAsync(
                    ConversionRequest.FromFile(file, outputPath), cancellationToken);

                if (!result.Success)
                {
                    // Keep going: one unreadable file shouldn't cost you the other twenty-nine.
                    Console.Error.WriteLine($"Failed: {Path.GetFileName(file)} — {result.ErrorMessage}");
                    failed++;
                    continue;
                }

                converted++;
                if (result.OutputPath is not null)
                    Console.WriteLine($"Saved to: {result.OutputPath}");
                else
                    Console.Write(result.MarkdownContent);
            }

            if (resolution.Files.Count > 1)
                Console.WriteLine(failed == 0
                    ? $"Converted {converted} file(s)."
                    : $"Converted {converted} file(s), {failed} failed.");

            return failed > 0 || resolution.NotFound.Count > 0 || resolution.Unreadable.Count > 0 ? 1 : 0;
        });

        return command;
    }

    /// <summary>
    /// Decides whether --output names a file or a folder, based on what was typed rather than on how
    /// many files happened to match: asking for <c>*.pdf -o out</c> should always fill a folder called
    /// "out", whether the pattern catches one document or twenty.
    /// </summary>
    private static string? ResolveOutputFolder(string? output, string[] inputs, int matchCount)
    {
        if (output is null) return null;

        var singleNamedFile = inputs.Length == 1 && matchCount == 1 && File.Exists(inputs[0].Trim().Trim('"'));
        var looksLikeFolder =
            Directory.Exists(output) ||
            output.EndsWith(Path.DirectorySeparatorChar) ||
            output.EndsWith(Path.AltDirectorySeparatorChar);

        return singleNamedFile && !looksLikeFolder ? null : output;
    }

    private static string BuildOutputPath(string sourcePath, string? output, string? outputFolder)
    {
        var markdownName = Path.GetFileNameWithoutExtension(sourcePath) + ".md";

        if (outputFolder is not null)
            return Path.GetFullPath(Path.Combine(outputFolder, markdownName));

        // A single named file: --output is the exact file to write, otherwise sit next to the original.
        return Path.GetFullPath(output ?? Path.Combine(Path.GetDirectoryName(sourcePath)!, markdownName));
    }
}
