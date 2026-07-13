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
        VersionGateService versionGate)
    {
        var inputArg = new Argument<FileInfo>("input") { Description = "Path to the file to convert" };
        var outputOpt = new Option<FileInfo?>("--output", new string[] { "-o" }) { Description = "Output .md file path (defaults to stdout)" };

        var command = new Command("convert", "Convert a document to Markdown")
        {
            inputArg,
            outputOpt
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg)!;
            var output = parseResult.GetValue(outputOpt);

            try
            {
                var manifest = await manifestProvider.GetManifestAsync(cancellationToken);
                var envInfo = await environmentManager.GetEnvironmentInfoAsync(cancellationToken);

                if (!envInfo.IsReady)
                {
                    Console.Error.WriteLine($"Error: {envInfo.MissingReason}");
                    Environment.Exit(1);
                    return;
                }

                versionGate.ThrowIfIncompatible(envInfo.InstalledMarkItDownVersion!, manifest);
            }
            catch (VersionGateException ex)
            {
                Console.Error.WriteLine($"Version gate blocked: {ex.Message}");
                Environment.Exit(1);
                return;
            }
            catch (ManifestException ex)
            {
                Console.Error.WriteLine($"Warning: Could not verify manifest ({ex.Message}). Proceeding with installed version.");
            }

            var request = ConversionRequest.FromFile(input.FullName, output?.FullName);
            var result = await converter.ConvertAsync(request, cancellationToken);

            if (!result.Success)
            {
                Console.Error.WriteLine($"Conversion failed: {result.ErrorMessage}");
                Environment.Exit(1);
                return;
            }

            if (result.OutputPath is not null)
                Console.WriteLine($"Saved to: {result.OutputPath}");
            else
                Console.Write(result.MarkdownContent);
        });

        return command;
    }
}
