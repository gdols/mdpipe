using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MdPipe.Core.Exceptions;
using MdPipe.Core.Interfaces;
using MdPipe.Core.Models;
using MdPipe.Core.Services;
using MdPipe.Wpf.Mvvm;

namespace MdPipe.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SetupOrchestrator _setupOrchestrator;
    private readonly IMarkItDownConverter _converter;
    private readonly IPythonEnvironmentManager _environmentManager;

    private bool _isBusy;
    private bool _isReady;
    private bool _isConverting;
    private string _statusMessage = "Starting…";
    private string? _outputFolder;
    private CancellationTokenSource? _convertCts;
    private readonly UserSettings _settings;
    private readonly InputResolver _inputResolver;
    private readonly FormatCatalogProvider _formats;
    private bool _includeEverything;

    public MainViewModel(
        SetupOrchestrator setupOrchestrator,
        IMarkItDownConverter converter,
        IPythonEnvironmentManager environmentManager,
        InputResolver inputResolver,
        FormatCatalogProvider formats)
    {
        _setupOrchestrator = setupOrchestrator;
        _converter = converter;
        _environmentManager = environmentManager;
        _inputResolver = inputResolver;
        _formats = formats;

        Files.CollectionChanged += (_, _) => CommandManagerRefresh();

        ConvertCommand = new RelayCommand(async () => await ConvertAllAsync(), () => CanConvert);
        ClearCommand = new RelayCommand(() => Files.Clear(), () => Files.Count > 0 && !IsBusy);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => HasConvertedFiles);
        ChooseOutputFolderCommand = new RelayCommand(ChooseOutputFolder, () => !IsBusy);
        ReinstallCommand = new RelayCommand(async () => await ReinstallAsync(), () => !IsBusy);
        CancelCommand = new RelayCommand(() => _convertCts?.Cancel(), () => IsConverting);

        _settings = UserSettings.Load();
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
        ? "Next to each original file"
        : OutputFolder;

    public bool CanConvert => IsReady && !IsBusy && Files.Count > 0;

    public bool ShowReinstall => !IsReady && !IsBusy;

    private bool HasConvertedFiles => Files.Any(f => f.IsDone);

    public Task InitializeAsync() => PrepareEnvironmentAsync(forceReinstall: false);

    private async Task ReinstallAsync()
    {
        StatusMessage = "Reinstalling the environment…";
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
            StatusMessage = $"Ready · MarkItDown {result.Version}";
        }
        catch (PythonNotFoundException)
        {
            IsReady = false;
            StatusMessage = "Python is missing. Install Python 3.10 or later and reopen the app.";
            MessageBox.Show(
                "MdPipe needs Python 3.10 or later installed on the system.\n\n" +
                "Download it for free from python.org, install it, and reopen MdPipe.",
                "Python missing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (PythonEnvironmentException ex)
        {
            var envInfo = await _environmentManager.GetEnvironmentInfoAsync();
            if (envInfo.IsReady && envInfo.InstalledMarkItDownVersion is not null)
            {
                IsReady = true;
                StatusMessage = $"Ready · MarkItDown {envInfo.InstalledMarkItDownVersion}";
            }
            else
            {
                IsReady = false;
                StatusMessage = "Couldn't finish setting up MarkItDown.";
                MessageBox.Show(
                    "MdPipe couldn't finish its first-time setup.\n\n" + ex.Message + "\n\n" +
                    "The first run needs internet to download from python.org and PyPI. On a company " +
                    "network, a proxy, firewall, VPN or antivirus can block it.",
                    "Setup couldn't finish",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (MdPipeException ex)
        {
            IsReady = false;
            StatusMessage = "Couldn't prepare MarkItDown.";
            MessageBox.Show(ex.Message, "Error preparing MdPipe", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            IsReady = false;
            StatusMessage = "Couldn't finish setup.";
            MessageBox.Show(
                "MdPipe couldn't finish setting up.\n\n" + ex.Message,
                "Setup couldn't finish", MessageBoxButton.OK, MessageBoxImage.Error);
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
                ? "Skipped 1 folder you don't have permission to read."
                : $"Skipped {resolution.Unreadable.Count} folders you don't have permission to read.";
    }

    private async Task ConvertAllAsync()
    {
        IsBusy = true;
        IsConverting = true;
        StatusMessage = "Converting files…";
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

            var renamedNote = renamed > 0 ? $" · {renamed} renamed to avoid overwriting" : "";
            StatusMessage = cancelled
                ? $"Cancelled · {converted} file(s) converted so far{renamedNote}"
                : converted == pending.Count
                    ? $"Done · {converted} file(s) converted{renamedNote}"
                    : $"Finished with warnings · {converted}/{pending.Count} converted{renamedNote}";
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
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where to save the Markdown files"
        };
        if (dialog.ShowDialog() == true)
            OutputFolder = dialog.FolderName;
    }

    private void OpenOutputFolder()
    {
        var firstDone = Files.FirstOrDefault(f => f.IsDone && f.OutputPath is not null);
        var folder = firstDone?.OutputPath is { } p
            ? Path.GetDirectoryName(p)
            : OutputFolder;

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
    }

    private static void CommandManagerRefresh() =>
        Application.Current?.Dispatcher.Invoke(System.Windows.Input.CommandManager.InvalidateRequerySuggested);
}
