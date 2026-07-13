using MdPipe.Core.Interfaces;
using MdPipe.Core.Services;
using MdPipe.Infrastructure.MarkItDown;
using MdPipe.Infrastructure.Manifest;
using MdPipe.Infrastructure.Python;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMdPipeInfrastructure(this IServiceCollection services, string manifestUrl)
    {
        services.AddSingleton<VersionGateService>();
        services.AddSingleton<SetupOrchestrator>();
        services.AddSingleton<PythonEnvironmentManager>();
        services.AddSingleton<IPythonEnvironmentManager>(sp => sp.GetRequiredService<PythonEnvironmentManager>());
        services.AddSingleton<IMarkItDownConverter, MarkItDownConverter>();

        services.AddHttpClient<GitHubManifestProvider>(client =>
        {
            client.BaseAddress = new Uri(manifestUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MdPipe/1.0");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<EmbeddedManifestProvider>();
        services.AddSingleton<IManifestProvider>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var cached = new CachedManifestProvider(
                sp.GetRequiredService<GitHubManifestProvider>(),
                loggerFactory.CreateLogger<CachedManifestProvider>());

            return new FallbackManifestProvider(
                primary: cached,
                fallback: sp.GetRequiredService<EmbeddedManifestProvider>(),
                logger: loggerFactory.CreateLogger<FallbackManifestProvider>());
        });

        return services;
    }
}
