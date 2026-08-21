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

            // Destinations are worked out up front so the whole batch can go to the worker in one go,
            // which is what lets a single Python process serve all of them.
            var outputPaths = new OutputPathResolver();
            var destinations = new List<OutputPath?>(resolution.Files.Count);
            var requests = new List<ConversionRequest>(resolution.Files.Count);
            var renamed = 0;

            foreach (var file in resolution.Files)
            {
                OutputPath? destination = null;
                if (!toStdout)
                {
                    // An explicit --output file is the exact path the user asked for; anything else goes
                    // through the resolver, which keeps a batch from writing twice to the same name.
                    destination = outputFolder is null && output is not null
                        ? new OutputPath(Path.GetFullPath(output), false)
                        : outputPaths.For(file, outputFolder);
                    if (destination.Renamed) renamed++;
                }

                destinations.Add(destination);
                requests.Add(ConversionRequest.FromFile(file, destination?.FullPath));
            }

            var converted = 0;
            var failed = 0;
            var index = 0;

            await foreach (var result in converter.ConvertManyAsync(requests, cancellationToken))
            {
                var file = resolution.Files[index];
                var destination = destinations[index];
                index++;

                if (!result.Success)
                {
                    // Keep going: one unreadable file shouldn't cost you the other twenty-nine.
                    Console.Error.WriteLine($"Failed: {Path.GetFileName(file)} — {result.ErrorMessage}");
                    failed++;
                    continue;
                }

                converted++;
                if (result.OutputPath is not null)
                    Console.WriteLine(destination?.Renamed == true
                        ? $"Saved to: {result.OutputPath}  (renamed, {Path.GetFileNameWithoutExtension(file)}.md was already taken in this run)"
                        : $"Saved to: {result.OutputPath}");
                else
                    Console.Write(result.MarkdownContent);
            }

            if (resolution.Files.Count > 1)
            {
                var summary = failed == 0
                    ? $"Converted {converted} file(s)."
                    : $"Converted {converted} file(s), {failed} failed.";
                if (renamed > 0)
                    summary += $" {renamed} renamed to avoid overwriting another.";
                Console.WriteLine(summary);
            }

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
}
