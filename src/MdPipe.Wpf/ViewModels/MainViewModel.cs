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

public sealed class MainViewModel : ObservableObject
{
    private readonly SetupOrchestrator _setupOrchestrator;
    private readonly IMarkItDownConverter _converter;
    private readonly IPythonEnvironmentManager _environmentManager;

    private bool _isBusy;
    private bool _isReady;
    private bool _isConverting;
    private string _statusMessage = Strings.Starting;
    private string? _outputFolder;
    private CancellationTokenSource? _convertCts;
    private readonly UserSettings _settings;
    private readonly InputResolver _inputResolver;
    private readonly FormatCatalogProvider _formats;
    private readonly IDialogService _dialogs;
    private bool _includeEverything;

    public MainViewModel(
        SetupOrchestrator setupOrchestrator,
        IMarkItDownConverter converter,
        IPythonEnvironmentManager environmentManager,
        InputResolver inputResolver,
        FormatCatalogProvider formats,
        IDialogService dialogs,
        UserSettings settings)
    {
        _setupOrchestrator = setupOrchestrator;
        _converter = converter;
        _environmentManager = environmentManager;
        _inputResolver = inputResolver;
        _formats = formats;
        _dialogs = dialogs;
        _settings = settings;

        Files.CollectionChanged += (_, _) => CommandManagerRefresh();

        ConvertCommand = new RelayCommand(async () => await ConvertAllAsync(), () => CanConvert);
        ClearCommand = new RelayCommand(() => Files.Clear(), () => Files.Count > 0 && !IsBusy);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => HasConvertedFiles);
        ChooseOutputFolderCommand = new RelayCommand(ChooseOutputFolder, () => !IsBusy);
        ReinstallCommand = new RelayCommand(async () => await ReinstallAsync(), () => !IsBusy);
        CancelCommand = new RelayCommand(() => _convertCts?.Cancel(), () => IsConverting);

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

    public bool IsConverting
    {
        get => _isConverting;
        private set
        {
            if (SetProperty(ref _isConverting, value))
                CommandManagerRefresh();
        }
    }

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

    private async Task PrepareEnvironmentAsync(bool forceReinstall)
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var result = await Task.Run(() => _setupOrchestrator.RunAsync(forceReinstall, progress));

            IsReady = true;
            StatusMessage = string.Format(Strings.ReadyWithVersion, result.Version);
        }
        catch (PythonNotFoundException)
        {
            IsReady = false;
            StatusMessage = Strings.PythonMissingStatus;
            _dialogs.ShowMessage(Strings.PythonMissingBody, Strings.PythonMissingTitle, DialogKind.Warning);
        }
        catch (PythonEnvironmentException ex)
        {
            var envInfo = await _environmentManager.GetEnvironmentInfoAsync();
            if (envInfo.IsReady && envInfo.InstalledMarkItDownVersion is not null)
            {
                IsReady = true;
                StatusMessage = string.Format(Strings.ReadyWithVersion, envInfo.InstalledMarkItDownVersion);
            }
            else
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
            IsReady = false;
            StatusMessage = Strings.PrepareFailedStatus;
            _dialogs.ShowMessage(ex.Message, Strings.PrepareFailedTitle, DialogKind.Error);
        }
        catch (Exception ex)
        {
            IsReady = false;
            StatusMessage = Strings.SetupFailedStatus;
            _dialogs.ShowMessage(
                string.Format(Strings.SetupFailedBody, ex.Message),
                Strings.SetupUnfinishedTitle, DialogKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;

        var existing = Files.Select(f => f.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolution = _inputResolver.Resolve(paths, recursive: true, includeEverything: IncludeEverything);

        foreach (var file in resolution.Files)
        {
            if (existing.Add(file))
                Files.Add(new FileItemViewModel(file));
        }

        // Folders we couldn't open would otherwise vanish without a trace, and a partial list of files
        // looks exactly like a complete one.
        if (resolution.Unreadable.Count > 0)
            StatusMessage = resolution.Unreadable.Count == 1
                ? Strings.SkippedFolderOne
                : string.Format(Strings.SkippedFolderMany, resolution.Unreadable.Count);
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
