using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using MdPipe.Wpf.Mvvm;
using MdPipe.Wpf.Resources;
using MdPipe.Wpf.Services;

namespace MdPipe.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject, IUpdateHost
{
    private readonly SetupOrchestrator _setupOrchestrator;
    private readonly IMarkItDownConverter _converter;
    private readonly IPythonEnvironmentManager _environmentManager;

    private bool _isBusy;
    private bool _isReady;
    private bool _isConverting;
    private bool _isScanning;
    private string _statusMessage = Strings.Starting;
    private string? _outputFolder;
    private CancellationTokenSource? _convertCts;
    private CancellationTokenSource? _scanCts;
    private int _activeScans;
    private string _statusBeforeScan = string.Empty;
    private readonly UserSettings _settings;
    private readonly InputResolver _inputResolver;
    private readonly FormatCatalogProvider _formats;
    private readonly IDialogService _dialogs;
    private readonly string? _runningVersion;
    private bool _includeEverything;

    public MainViewModel(
        SetupOrchestrator setupOrchestrator,
        IMarkItDownConverter converter,
        IPythonEnvironmentManager environmentManager,
        InputResolver inputResolver,
        FormatCatalogProvider formats,
        IDialogService dialogs,
        AppUpdateService updates,
        IAppUpdateInstaller updateInstaller,
        UserSettings settings,
        string? runningVersion = null)
    {
        _setupOrchestrator = setupOrchestrator;
        _converter = converter;
        _environmentManager = environmentManager;
        _inputResolver = inputResolver;
        _formats = formats;
        _dialogs = dialogs;
        _settings = settings;
        _runningVersion = runningVersion;

        Files.CollectionChanged += (_, _) => CommandManagerRefresh();

        ConvertCommand = new RelayCommand(async () => await ConvertAllAsync(), () => CanConvert);
        ClearCommand = new RelayCommand(() => Files.Clear(), () => Files.Count > 0 && !IsBusy);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => HasConvertedFiles);
        ChooseOutputFolderCommand = new RelayCommand(ChooseOutputFolder, () => !IsBusy);
        ReinstallCommand = new RelayCommand(async () => await ReinstallAsync(), () => !IsBusy);
        // One button for both waits: whichever of the two is running is the one the user is staring at.
        CancelCommand = new RelayCommand(
            () =>
            {
                _convertCts?.Cancel();
                _scanCts?.Cancel();
            },
            () => CanCancel);
        UpdateNotice = new UpdateNoticeViewModel(updates, updateInstaller, dialogs, this, runningVersion);

        if (!string.IsNullOrEmpty(_settings.OutputFolder) && Directory.Exists(_settings.OutputFolder))
            _outputFolder = _settings.OutputFolder;
    }

    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    public RelayCommand ConvertCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand ChooseOutputFolderCommand { get; }
    public RelayCommand ReinstallCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>The bar offering a newer MdPipe, which looks after itself.</summary>
    public UpdateNoticeViewModel UpdateNotice { get; }

    public bool IsConverting
    {
        get => _isConverting;
        private set
        {
            if (SetProperty(ref _isConverting, value))
            {
                OnPropertyChanged(nameof(CanCancel));
                CommandManagerRefresh();
            }
        }
    }

    /// <summary>A folder is being walked, which on a network share can outlast the conversion.</summary>
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(CanCancel));
                CommandManagerRefresh();
            }
        }
    }

    public bool CanCancel => IsConverting || IsScanning;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(ShowReinstall));
                CommandManagerRefresh();
            }
        }
    }

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (SetProperty(ref _isReady, value))
            {
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(ShowReinstall));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetProperty(ref _outputFolder, value))
            {
                OnPropertyChanged(nameof(OutputFolderDisplay));
                _settings.OutputFolder = value;
                _settings.Save();
            }
        }
    }

    /// <summary>
    /// Try every file instead of only the known formats. Off by default, or scanning an ordinary
    /// folder would fill the list with .exe and .dll.
    /// </summary>
    public bool IncludeEverything
    {
        get => _includeEverything;
        set => SetProperty(ref _includeEverything, value);
    }

    /// <summary>What the installed engine says it can read, for the formats window.</summary>
    public FormatCatalog Formats => _formats.Get();

    public string OutputFolderDisplay => string.IsNullOrEmpty(OutputFolder)
        ? Strings.NextToEachOriginal
        : OutputFolder;

    public bool CanConvert => IsReady && !IsBusy && Files.Count > 0;

    public bool ShowReinstall => !IsReady && !IsBusy;

    private bool HasConvertedFiles => Files.Any(f => f.IsDone);

    public Task InitializeAsync() => PrepareEnvironmentAsync(forceReinstall: false);

    private async Task ReinstallAsync()
    {
        StatusMessage = Strings.Reinstalling;
        await PrepareEnvironmentAsync(forceReinstall: true);
    }

    /// <summary>
    /// Gets the engine ready, and either way works out whether to mention a newer MdPipe. Somebody
    /// looking at a failed start is the person most likely to be helped by hearing a newer version
    /// exists, so no path here leaves without deciding.
    /// </summary>
    private async Task PrepareEnvironmentAsync(bool forceReinstall)
    {
        AppRelease? announced = null;
        var runFinished = false;

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var result = await Task.Run(() => _setupOrchestrator.RunAsync(forceReinstall, progress));

            IsReady = true;
            StatusMessage = string.Format(Strings.ReadyWithVersion, result.Version);

            // Came back with the setup, so it cost nothing extra and works the same offline.
            announced = result.App;
            runFinished = true;
        }
        catch (PythonNotFoundException)
        {
            IsReady = false;
            StatusMessage = Strings.PythonMissingStatus;
            _dialogs.ShowMessage(Strings.PythonMissingBody, Strings.PythonMissingTitle, DialogKind.Warning);
        }
        catch (PythonEnvironmentException ex)
        {
            if (!await UsableEnvironmentSurvivedAsync())
            {
                IsReady = false;
                StatusMessage = Strings.SetupUnfinishedStatus;
                _dialogs.ShowMessage(
                    string.Format(Strings.SetupUnfinishedBody, ex.Message),
                    Strings.SetupUnfinishedTitle, DialogKind.Warning);
            }
        }
        catch (MdPipeException ex)
        {
            if (!await UsableEnvironmentSurvivedAsync())
            {
                IsReady = false;
                StatusMessage = Strings.PrepareFailedStatus;
                _dialogs.ShowMessage(ex.Message, Strings.PrepareFailedTitle, DialogKind.Error);
            }
        }
        catch (Exception ex)
        {
            if (!await UsableEnvironmentSurvivedAsync())
            {
                IsReady = false;
                StatusMessage = Strings.SetupFailedStatus;
                _dialogs.ShowMessage(
                    string.Format(Strings.SetupFailedBody, ex.Message),
                    Strings.SetupUnfinishedTitle, DialogKind.Error);
            }
        }
        finally
        {
            IsBusy = false;
        }

        // One request, only on a path that was already going slowly. None at all on the happy one.
        if (!runFinished) announced = await LatestReleaseOrNothingAsync();

        UpdateNotice.Consider(announced, mightBeTheFix: !IsReady);
    }

    /// <summary>
    /// Reached when something has already gone wrong, so it must not throw on top of it. Losing the
    /// answer costs the notice, which is where we were anyway.
    /// </summary>
    private async Task<AppRelease?> LatestReleaseOrNothingAsync()
    {
        try
        {
            return await _setupOrchestrator.LatestReleaseAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Setup failed. Before saying so, ask the engine on disk whether it works anyway: refusing to
    /// convert because a version check timed out is the wrong trade.
    /// </summary>
    /// <returns>True when the app ended up usable after all.</returns>
    private async Task<bool> UsableEnvironmentSurvivedAsync()
    {
        try
        {
            var envInfo = await _environmentManager.GetEnvironmentInfoAsync();
            if (!envInfo.IsReady || envInfo.InstalledMarkItDownVersion is null) return false;

            IsReady = true;
            StatusMessage = string.Format(Strings.ReadyWithVersion, envInfo.InstalledMarkItDownVersion);
            return true;
        }
        catch (Exception)
        {
            // The recovery attempt itself failing just means there is nothing to recover.
            return false;
        }
    }

    /// <summary>
    /// Expands what was dropped and adds the documents to the list. The walk runs off the UI thread,
    /// or a big folder on a network share freezes the window. Only a conversion blocks this: the
    /// first run spends minutes downloading Python and a drop during it should still land.
    /// </summary>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (IsConverting) return;

        // A drop during another scan joins it and shares its cancellation. A drop with nothing
        // running starts a fresh token, so cancelling once doesn't poison the next one.
        if (_activeScans == 0)
        {
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();
            _statusBeforeScan = StatusMessage;
        }

        var token = _scanCts!.Token;
        _activeScans++;
        IsScanning = true;

        InputResolution resolution;
        try
        {
            var progress = new Progress<int>(found =>
                StatusMessage = found == 0 ? Strings.Scanning : string.Format(Strings.ScanningFound, found));

            resolution = await Task.Run(
                () => _inputResolver.Resolve(paths, recursive: true, IncludeEverything, progress, token), token);
        }
        catch (OperationCanceledException)
        {
            // Cancelled before the walk even started. Resolve itself returns partial results instead
            // of throwing, so this only covers the race at the very beginning.
            resolution = new InputResolution([], [], [], Cancelled: true);
        }
        finally
        {
            if (--_activeScans == 0)
            {
                IsScanning = false;
                _scanCts?.Dispose();
                _scanCts = null;
            }
        }

        // Built after the walk, not before: two folders dropped together scan at the same time, and
        // the second has to see what the first added.
        var existing = Files.Select(f => f.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var file in resolution.Files)
        {
            if (!existing.Add(file)) continue;
            Files.Add(new FileItemViewModel(file));
            added++;
        }

        StatusMessage = Summarize(resolution, added, _statusBeforeScan);
    }

    /// <summary>
    /// What the status bar says once a scan finishes. Anything worth knowing wins; otherwise the
    /// previous message comes back, since "Ready" beats a count that stopped moving.
    /// </summary>
    private static string Summarize(InputResolution resolution, int added, string previous)
    {
        if (resolution.Cancelled)
            return string.Format(Strings.ScanCancelled, added);

        // A partial list looks exactly like a complete one, so folders we couldn't open must be said.
        if (resolution.Unreadable.Count > 0)
            return resolution.Unreadable.Count == 1
                ? Strings.SkippedFolderOne
                : string.Format(Strings.SkippedFolderMany, resolution.Unreadable.Count);

        return previous;
    }

    private async Task ConvertAllAsync()
    {
        IsBusy = true;
        IsConverting = true;
        StatusMessage = Strings.ConvertingFiles;
        _convertCts = new CancellationTokenSource();
        var cancelled = false;

        try
        {
            var token = _convertCts.Token;
            var pending = Files.Where(f => f.Status is FileStatus.Pending or FileStatus.Error).ToList();
            // One resolver per batch, so two files with the same name landing in the same output folder
            // don't quietly overwrite each other.
            var outputPaths = new OutputPathResolver();
            var converted = 0;
            var renamed = 0;

            // Destinations first: the batch goes to the worker in one go, which is what lets one
            // Python process handle all of them instead of paying the two-second import per file.
            var requests = new List<ConversionRequest>(pending.Count);
            foreach (var file in pending)
            {
                file.ErrorMessage = null;
                var destination = outputPaths.For(file.SourcePath, OutputFolder);
                if (destination.Renamed) renamed++;
                requests.Add(ConversionRequest.FromFile(file.SourcePath, destination.FullPath));
            }

            var index = 0;
            if (pending.Count > 0) pending[0].Status = FileStatus.Converting;

            try
            {
                await foreach (var result in _converter.ConvertManyAsync(requests, token))
                {
                    var file = pending[index];
                    index++;

                    if (result.Success)
                    {
                        file.OutputPath = result.OutputPath;
                        file.Status = FileStatus.Done;
                        converted++;
                    }
                    else
                    {
                        file.ErrorMessage = result.ErrorMessage;
                        file.Status = FileStatus.Error;
                    }

                    if (index < pending.Count) pending[index].Status = FileStatus.Converting;
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                // Whatever went wrong belongs to the file that was in flight, not to the whole list.
                if (index < pending.Count)
                {
                    pending[index].ErrorMessage = ex.Message;
                    pending[index].Status = FileStatus.Error;
                    index++;
                }
            }

            // Anything still marked as converting never got its turn.
            for (var i = index; i < pending.Count; i++)
                if (pending[i].Status == FileStatus.Converting)
                    pending[i].Status = FileStatus.Pending;

            var renamedNote = renamed > 0 ? string.Format(Strings.RenamedNote, renamed) : "";
            StatusMessage = (cancelled
                ? string.Format(Strings.CancelledCount, converted)
                : converted == pending.Count
                    ? string.Format(Strings.DoneCount, converted)
                    : string.Format(Strings.FinishedWithWarnings, converted, pending.Count)) + renamedNote;
        }
        finally
        {
            _convertCts.Dispose();
            _convertCts = null;
            IsConverting = false;
            IsBusy = false;
        }
    }

    // --- IUpdateHost: the little the notice needs from the window it sits in ---

    void IUpdateHost.SetBusy(bool busy) => IsBusy = busy;

    void IUpdateHost.Report(string status) => StatusMessage = status;

    private void ChooseOutputFolder()
    {
        if (_dialogs.PickFolder(Strings.ChooseOutputTitle) is { } folder)
            OutputFolder = folder;
    }

    private void OpenOutputFolder()
    {
        var firstDone = Files.FirstOrDefault(f => f.IsDone && f.OutputPath is not null);
        var folder = firstDone?.OutputPath is { } p
            ? Path.GetDirectoryName(p)
            : OutputFolder;

        if (!string.IsNullOrEmpty(folder))
            _dialogs.OpenFolder(folder);
    }

    private static void CommandManagerRefresh() =>
        Application.Current?.Dispatcher.Invoke(System.Windows.Input.CommandManager.InvalidateRequerySuggested);
}
