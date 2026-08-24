using System.Windows;
using MdPipe.Infrastructure.DependencyInjection;
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

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                services.AddMdPipeInfrastructure(ManifestUrl);
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton(UserSettings.Load());
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "An unexpected error occurred:\n\n" + args.Exception.Message,
                "MdPipe", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = _host.Services.GetRequiredService<MainWindow>();
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();
        window.DataContext = viewModel;
        window.Show();

        // Files dropped on the executable, or opened with it, arrive as arguments. Both jobs start
        // together on purpose: preparing the environment can spend minutes downloading Python on a
        // first run, and there is no reason to stare at an empty list while it does.
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
