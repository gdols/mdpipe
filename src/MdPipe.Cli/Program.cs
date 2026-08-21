using System.CommandLine;
using MdPipe.Cli.Commands;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Services;
using MdPipe.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var manifestUrl = "https://raw.githubusercontent.com/gdols/MdPipe/master/manifest/markitdown-compat.json";

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole(opts => opts.FormatterName = "simple");
        logging.SetMinimumLevel(LogLevel.Warning);

        // The convert command already reports each file's outcome, so the converter's own warning
        // would just say the same thing twice. Everything else (manifest, Python setup) still shows.
        logging.AddFilter("MdPipe.Infrastructure.MarkItDown.MarkItDownConverter", LogLevel.Error);
    })
    .ConfigureServices(services =>
    {
        services.AddMdPipeInfrastructure(manifestUrl);
    })
    .Build();

var converter = host.Services.GetRequiredService<IMarkItDownConverter>();
var environmentManager = host.Services.GetRequiredService<IPythonEnvironmentManager>();
var manifestProvider = host.Services.GetRequiredService<IManifestProvider>();
var versionGate = host.Services.GetRequiredService<VersionGateService>();
var orchestrator = host.Services.GetRequiredService<SetupOrchestrator>();
var inputResolver = host.Services.GetRequiredService<InputResolver>();
var formatCatalog = host.Services.GetRequiredService<FormatCatalogProvider>();

var root = new RootCommand("MdPipe: convert documents to Markdown using Microsoft MarkItDown")
{
    ConvertCommand.Build(converter, environmentManager, manifestProvider, versionGate, inputResolver),
    SetupCommand.Build(orchestrator),
    StatusCommand.Build(environmentManager, manifestProvider, versionGate),
    FormatsCommand.Build(formatCatalog)
};

return await root.Parse(args).InvokeAsync();
