using System.Reflection;
using System.Windows;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Services;
using MdPipe.Infrastructure.DependencyInjection;
using MdPipe.Infrastructure.Logging;
using MdPipe.Wpf.Services;
using MdPipe.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MdPipe.Wpf;

public partial class App : Application
{
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/gdols/MdPipe/master/manifest/markitdown-compat.json";

    private readonly IHost _host;

    /// <summary>
    /// What this build calls itself. Comes from the Version in Directory.Build.props, which the
    /// release workflow checks against the tag.
    /// </summary>
    private static string? RunningVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : null;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // A small file in %APPDATA%\mdpipe, so a failure leaves something to attach to an issue.
                // The framework's own chatter (every HTTP request, the host starting) stays out.
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddFilter("Microsoft", LogLevel.Warning);
                logging.AddFilter("System", LogLevel.Warning);
                logging.AddProvider(new FileLoggerProvider());
            })
            .ConfigureServices(services =>
            {
                services.AddMdPipeInfrastructure(ManifestUrl);
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton(UserSettings.Load());
                services.AddSingleton(sp => new MainViewModel(
                    sp.GetRequiredService<SetupOrchestrator>(),
                    sp.GetRequiredService<IMarkItDownConverter>(),
                    sp.GetRequiredService<IPythonEnvironmentManager>(),
                    sp.GetRequiredService<InputResolver>(),
                    sp.GetRequiredService<FormatCatalogProvider>(),
                    sp.GetRequiredService<IDialogService>(),
                    sp.GetRequiredService<AppUpdateService>(),
                    sp.GetRequiredService<IAppUpdateInstaller>(),
                    sp.GetRequiredService<UserSettings>(),
                    RunningVersion,
                    sp.GetRequiredService<ILogger<MainViewModel>>()));
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var log = _host.Services.GetRequiredService<ILogger<App>>();
        log.LogInformation("MdPipe {Version} starting on {OS}, {Culture}.",
            RunningVersion, Environment.OSVersion.VersionString, System.Globalization.CultureInfo.CurrentUICulture.Name);

        DispatcherUnhandledException += (_, args) =>
        {
            log.LogError(args.Exception, "Unexpected error.");
            MessageBox.Show(
                "An unexpected error occurred:\n\n" + args.Exception.Message +
                string.Format(MdPipe.Wpf.Resources.Strings.LogHint, FileLoggerProvider.DefaultPath),
                "MdPipe", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Windows will not delete a running executable, so the version replaced by the last update
        // is still sitting next to this one. Now is the first moment it can go.
        _host.Services.GetRequiredService<IAppUpdateInstaller>().CleanUpPreviousUpdate();

        var window = _host.Services.GetRequiredService<MainWindow>();
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();
        window.DataContext = viewModel;
        window.Show();

        // Files dropped on the exe arrive as arguments. Both jobs start together on purpose: a
        // first run spends minutes downloading Python and there is no reason to stare at an empty
        // list while it does.
        var initialization = viewModel.InitializeAsync();

        if (e.Args.Length > 0)
            await viewModel.AddFilesAsync(e.Args);

        await initialization;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host.Dispose();
        base.OnExit(e);
    }
}
