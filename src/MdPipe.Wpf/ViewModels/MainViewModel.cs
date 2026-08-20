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

    public MainViewModel(
        SetupOrchestrator setupOrchestrator,
        IMarkItDownConverter converter,
        IPythonEnvironmentManager environmentManager,
        InputResolver inputResolver)
    {
        _setupOrchestrator = setupOrchestrator;
        _converter = converter;
        _environmentManager = environmentManager;
        _inputResolver = inputResolver;

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
        var resolution = _inputResolver.Resolve(paths, recursive: true);

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
            var converted = 0;

            foreach (var file in pending)
            {
                file.Status = FileStatus.Converting;
                file.ErrorMessage = null;

                try
                {
                    var outputPath = BuildOutputPath(file.SourcePath);
                    var request = ConversionRequest.FromFile(file.SourcePath, outputPath);
                    var result = await Task.Run(() => _converter.ConvertAsync(request, token), token);

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
                }
                catch (OperationCanceledException)
                {
                    file.Status = FileStatus.Pending;
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    file.ErrorMessage = ex.Message;
                    file.Status = FileStatus.Error;
                }
            }

            StatusMessage = cancelled
                ? $"Cancelled · {converted} file(s) converted so far"
                : converted == pending.Count
                    ? $"Done · {converted} file(s) converted"
                    : $"Finished with warnings · {converted}/{pending.Count} converted";
        }
        finally
        {
            _convertCts.Dispose();
            _convertCts = null;
            IsConverting = false;
            IsBusy = false;
        }
    }

    private string BuildOutputPath(string sourcePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + ".md";
        var targetDir = string.IsNullOrEmpty(OutputFolder)
            ? Path.GetDirectoryName(sourcePath)!
            : OutputFolder;
        return Path.Combine(targetDir, fileName);
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
