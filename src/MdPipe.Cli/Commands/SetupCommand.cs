using System.CommandLine;
using MdPipe.Core.Services;

namespace MdPipe.Cli.Commands;

public static class SetupCommand
{
    public static Command Build(SetupOrchestrator orchestrator)
    {
        var forceOpt = new Option<bool>("--force") { Description = "Reinstall even if already compatible" };

        var command = new Command("setup", "Prepare or update the MarkItDown environment")
        {
            forceOpt
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var force = parseResult.GetValue(forceOpt);

            var progress = new Progress<string>(Console.WriteLine);

            var result = await orchestrator.RunAsync(force, progress, cancellationToken);

            Console.WriteLine(result.WasInstalled
                ? $"MarkItDown {result.Version} installed successfully."
                : $"MarkItDown {result.Version} is already up to date.");
        });

        return command;
    }
}
