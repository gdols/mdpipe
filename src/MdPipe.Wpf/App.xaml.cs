using System.Windows;
using MdPipe.Infrastructure.DependencyInjection;
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

        // Files dropped on the executable, or opened with it, arrive as arguments. They have to go in
        // before the environment check starts, because that marks the view model busy and AddFiles
        // refuses to touch the list while a batch could be running.
        if (e.Args.Length > 0)
            viewModel.AddFiles(e.Args);

        await viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host.Dispose();
        base.OnExit(e);
    }
}
