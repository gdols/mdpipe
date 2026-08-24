using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using FluentAssertions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using MdPipe.Wpf.Services;
using MdPipe.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdPipe.Wpf.Tests;

/// <summary>
/// Builds the real windows and lets their bindings resolve.
/// </summary>
/// <remarks>
/// Written after shipping a release that could not open. `Run.Text` is one of the few dependency
/// properties WPF binds two-way by default, so pointing it at a get-only property throws the
/// moment the window is laid out. Every other test passed, because none of them ever touched the
/// XAML: the view model was exercised directly and the markup was taken on trust.
/// <para>
/// These do not assert anything about how the window looks. They assert that it opens, which is
/// the part that was silently not covered.
/// </para>
/// </remarks>
[Collection("ui-strings")]
public sealed class WindowBindingTests : IDisposable
{

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mdpipe-binding-tests", Guid.NewGuid().ToString("N"));

    public WindowBindingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    [Fact]
    public async Task TheMainWindowOpensWithAnUpdateWaiting()
    {
        // The state that broke: the update bar visible, so its bindings actually get evaluated.
        // With the bar hidden the faulty binding was never reached and everything looked fine.
        var viewModel = await BuildViewModelAsync(announcing: "9.9.9");

        OnUiThread(() => ForceLayout(new MainWindow { DataContext = viewModel }));
    }

    [Fact]
    public async Task TheMainWindowOpensWithNothingToReport()
    {
        var viewModel = await BuildViewModelAsync(announcing: null);

        OnUiThread(() => ForceLayout(new MainWindow { DataContext = viewModel }));
    }

    [Fact]
    public void TheAboutWindowOpens()
    {
        OnUiThread(() => ForceLayout(new AboutWindow()));
    }

    [Fact]
    public void TheFormatsWindowOpens()
    {
        var catalog = new FormatCatalog("0.1.7", [".pdf", ".docx"], [], IsBaseline: false);
        OnUiThread(() => ForceLayout(new FormatsWindow(catalog)));
    }

    /// <summary>
    /// Lays the window out without putting it on screen, which is what makes every binding
    /// evaluate. Showing it would need a message loop and would leave windows around in CI.
    /// </summary>
    private static void ForceLayout(Window window)
    {
        window.Measure(new Size(1024, 768));
        window.Arrange(new Rect(0, 0, 1024, 768));
        window.UpdateLayout();
    }

    /// <summary>
    /// Builds a view model in the state the window should be laid out in.
    /// </summary>
    /// <remarks>
    /// Deliberately not on the interface thread. Preparing the environment awaits, and its
    /// continuation wants the thread it started on, so blocking that thread while waiting for it
    /// deadlocks. The view model has no thread affinity of its own until a binding attaches.
    /// </remarks>
    private async Task<MainViewModel> BuildViewModelAsync(string? announcing)
    {
        var manifest = new StubManifest(announcing is null
            ? null
            : new AppRelease(announcing, "https://example.invalid/r", DownloadUrl: "https://example.invalid/e"));

        var formats = new FormatCatalogProvider(Path.Combine(_dir, "no-catalog.json"));
        var environment = new StubEnvironment();

        var viewModel = new MainViewModel(
            new SetupOrchestrator(manifest, manifest, environment, new VersionGateService(),
                NullLogger<SetupOrchestrator>.Instance),
            new StubConverter(),
            environment,
            new InputResolver(formats),
            formats,
            new StubDialogs(),
            new AppUpdateService(new VersionGateService()),
            new StubInstaller(),
            UserSettings.Load(Path.Combine(_dir, "settings.json")),
            "0.1.0");

        // The bar only appears once the environment check has run, so wait for it here rather than
        // laying out a window whose interesting half is still hidden.
        await viewModel.InitializeAsync();
        return viewModel;
    }

    /// <summary>
    /// Runs the action on the one interface thread these tests share.
    /// </summary>
    /// <remarks>
    /// One thread for the whole run, not one per test. WPF allows a single Application per process
    /// and refuses to have its ResourceAssembly set twice, so standing a fresh one up each time
    /// fails on the second test. Any exception is carried back so the test fails with the real
    /// reason rather than a timeout.
    /// </remarks>
    private static void OnUiThread(Action action)
    {
        Exception? failure = null;
        _dispatcherFailure = null;

        UiThread.Invoke(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });

        // Layout finishes on the dispatcher queue, after Invoke has already returned, so a binding
        // that cannot be established surfaces later and on that thread. Draining the queue at the
        // lowest priority waits for all of it; without this the check runs too early and a broken
        // binding passes, and without the handler the whole test host goes down instead of one
        // test failing with a reason.
        UiThread.Invoke(() => { }, DispatcherPriority.SystemIdle);

        if (failure is null && _dispatcherFailure is not null) failure = _dispatcherFailure;

        if (failure is not null) throw failure;
    }

    private static Exception? _dispatcherFailure;

    private static Dispatcher UiThread => UiThreadHolder.Value;

    private static readonly Lazy<Dispatcher> UiThreadHolder = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>();

        var thread = new Thread(() =>
        {
            // The palette the windows are built from, loaded straight from the assembly that ships
            // it. A plain Application rather than MdPipe's own: constructing that one builds the
            // dependency injection host, which has no business being alive in a test about markup.
            // The absolute pack URI names the assembly, so nothing depends on what the test host
            // decided the entry assembly was.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MdPipe;component/Resources/Theme.xaml", UriKind.Absolute)
            });

            Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
            {
                _dispatcherFailure = e.Exception;
                e.Handled = true;
            };

            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return ready.Task.GetAwaiter().GetResult();
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed class StubManifest(AppRelease? app) : IBuildManifestProvider, IManifestProvider
    {
        public Task<CompatibilityManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompatibilityManifest
            {
                StableVersion = "0.1.7",
                CompatibleVersions = new List<string> { "0.1.7" }.AsReadOnly(),
                App = app
            });
    }

    private sealed class StubEnvironment : IPythonEnvironmentManager
    {
        public Task<PythonEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PythonEnvironmentInfo { IsReady = true, InstalledMarkItDownVersion = "0.1.7" });

        public Task SetupAsync(string markItDownVersion, bool forceReinstall = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("0.1.7");

        public Task EnsureFormatCatalogAsync(string? installedVersion = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubConverter : IMarkItDownConverter
    {
        public async IAsyncEnumerable<ConversionResult> ConvertManyAsync(
            IReadOnlyList<ConversionRequest> requests,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<ConversionResult> ConvertAsync(
            ConversionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConversionResult.Ok(string.Empty, null));
    }

    private sealed class StubDialogs : IDialogService
    {
        public void ShowMessage(string message, string title, DialogKind kind) { }
        public bool Confirm(string message, string title) => false;
        public void OpenLink(string url) { }
        public void RestartWith(string executablePath) { }
        public string? PickFolder(string title) => null;
        public void OpenFolder(string path) { }
    }

    private sealed class StubInstaller : IAppUpdateInstaller
    {
        public bool CanInstall => true;

        public Task<string> InstallAsync(AppUpdate update, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult("");

        public void CleanUpPreviousUpdate() { }
    }
}
