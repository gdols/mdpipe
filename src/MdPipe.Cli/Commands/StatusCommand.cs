using System.CommandLine;
using System.Reflection;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Services;

namespace MdPipe.Cli.Commands;

public static class StatusCommand
{
    public static Command Build(
        IPythonEnvironmentManager environmentManager,
        IManifestProvider manifestProvider,
        VersionGateService versionGate,
        AppUpdateService appUpdates)
    {
        var command = new Command("status", "Show environment and version compatibility status");

        command.SetAction(async (_, cancellationToken) =>
        {
            var envInfo = await environmentManager.GetEnvironmentInfoAsync(cancellationToken);

            Console.WriteLine("=== MdPipe Status ===");
            Console.WriteLine();
            Console.WriteLine($"  Environment ready : {(envInfo.IsReady ? "yes" : "no")}");

            if (envInfo.PythonExecutable is not null)
                Console.WriteLine($"  Python executable : {envInfo.PythonExecutable}");

            if (envInfo.InstalledMarkItDownVersion is not null)
                Console.WriteLine($"  MarkItDown version: {envInfo.InstalledMarkItDownVersion}");
            else if (envInfo.MissingReason is not null)
                Console.WriteLine($"  Status            : {envInfo.MissingReason}");

            Console.WriteLine();

            try
            {
                var manifest = await manifestProvider.GetManifestAsync(cancellationToken);
                Console.WriteLine($"  Manifest stable   : {manifest.StableVersion}");
                Console.WriteLine($"  Manifest updated  : {manifest.UpdatedAt}");
                Console.WriteLine($"  Compatible set    : [{string.Join(", ", manifest.CompatibleVersions)}]");

                if (envInfo.InstalledMarkItDownVersion is not null)
                {
                    var compatible = versionGate.IsCompatible(envInfo.InstalledMarkItDownVersion, manifest);
                    Console.WriteLine($"  Version gate      : {(compatible ? "PASS" : "FAIL: run 'mdpipe setup'")}");
                }

                // Rides the manifest that was just fetched, so this costs no extra request.
                if (appUpdates.CheckFor(manifest, RunningVersion) is { } update)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  MdPipe {RunningVersion} is running; {update.Version} is out: {update.ReleaseUrl}");
                    if (!string.IsNullOrWhiteSpace(update.Notes))
                        Console.WriteLine($"  {update.Notes}");
                }
            }
            catch (ManifestException ex)
            {
                Console.WriteLine($"  Manifest          : unavailable ({ex.Message})");
            }

            Console.WriteLine();
        });

        return command;
    }

    /// <summary>The version of the tool doing the asking, from the single Version in Directory.Build.props.</summary>
    private static string? RunningVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : null;
}
