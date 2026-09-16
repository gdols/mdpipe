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

    /// <summary>
    /// A folder is being walked. Separate from <see cref="IsConverting"/> because the two overlap in
    /// nothing except needing a way out: scanning a network share can take longer than the conversion.
    /// </summary>
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
    /// Try every file a folder holds instead of only the known formats, letting the engine decide by
    /// content. Off by default, or scanning an ordinary folder would fill the list with .exe and .dll.
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
    /// Gets the engine ready, and either way works out whether to mention a newer MdPipe.
    /// </summary>
    /// <remarks>
    /// The notice used to be worked out only on the way through a run that finished, so a first
    /// launch that failed showed nothing. That is backwards: somebody looking at an error is the
    /// person most likely to be helped by hearing that a newer version exists, and the release that
    /// fixed the Python 3.14 failure could not reach any of the people it was written for.
    /// <para>
    /// No path may leave without deciding, which is why the failure branches no longer return early.
    /// </para>
    /// </remarks>
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

            // The manifest is already fetched, cached and falls back on its own, so learning whether
            // a newer MdPipe exists costs nothing extra and works the same when offline.
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

        // Only asked separately when the run did not get far enough to bring the answer back, which
        // is one request on a path that was already going slowly, and none at all on the happy one.
        if (!runFinished) announced = await LatestReleaseOrNothingAsync();

        UpdateNotice.Consider(announced, mightBeTheFix: !IsReady);
    }

    /// <summary>
    /// What the repository says the newest release is, or nothing at all.
    /// </summary>
    /// <remarks>
    /// Reached when something has already gone wrong, so it must not be able to make things worse
    /// by throwing on top. Losing the answer costs the notice, which is where we were anyway.
    /// </remarks>
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
    /// Preparing the environment failed. Before saying so, ask the engine on disk whether it works:
    /// if it does, that is the answer that matters. Checking the version is a nicety, converting is
    /// the job, and refusing to do the job because a version check timed out is the wrong trade.
    /// </summary>
    /// <returns>True when the app was put into a usable state and the caller should stop.</returns>
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
    /// Expands what was dropped and adds the documents to the list.
    /// </summary>
    /// <remarks>
    /// The walk runs off the UI thread. It used to run on it, which meant dropping a large folder, a
    /// network share or a OneDrive folder full of files that aren't downloaded yet froze the window
    /// with no progress and no way out.
    /// <para>
    /// Only a conversion blocks this. Preparing the environment no longer does: the first run spends
    /// minutes downloading Python, and a drop during that used to be discarded without a word.
    /// </para>
    /// </remarks>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (IsConverting) return;

        // A drop while another scan is running joins it and shares its cancellation; a drop with
        // nothing running always starts from a fresh token, so cancelling once doesn't poison the next.
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

        // Built here rather than before the walk: two folders dropped in quick succession scan at the
        // same time, and the second one has to see what the first one already added.
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
    /// What the status bar says once a scan finishes. Anything the user needs to know wins over the
    /// message that was there before; otherwise the previous one comes back, because "Ready" is more
    /// useful to look at than a stale count.
    /// </summary>
    private static string Summarize(InputResolution resolution, int added, string previous)
    {
        // A cancelled scan found whatever it found. Saying so beats leaving a count that stopped moving.
        if (resolution.Cancelled)
            return string.Format(Strings.ScanCancelled, added);

        // Folders we couldn't open would otherwise vanish without a trace, and a partial list of files
        // looks exactly like a complete one.
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

            // Destinations first: the whole batch goes to the worker at once, which is what lets a
            // single Python process handle all of them instead of paying the two-second import per file.
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
