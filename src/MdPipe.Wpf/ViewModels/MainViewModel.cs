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
    private string _statusMessage = "Starting…";
    private string? _outputFolder;

    public MainViewModel(
        SetupOrchestrator setupOrchestrator,
        IMarkItDownConverter converter,
        IPythonEnvironmentManager environmentManager)
    {
        _setupOrchestrator = setupOrchestrator;
        _converter = converter;
        _environmentManager = environmentManager;

        Files.CollectionChanged += (_, _) => CommandManagerRefresh();

        ConvertCommand = new RelayCommand(async () => await ConvertAllAsync(), () => CanConvert);
        ClearCommand = new RelayCommand(() => Files.Clear(), () => Files.Count > 0 && !IsBusy);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => HasConvertedFiles);
        ChooseOutputFolderCommand = new RelayCommand(ChooseOutputFolder, () => !IsBusy);
        ReinstallCommand = new RelayCommand(async () => await ReinstallAsync(), () => !IsBusy);
    }

    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    public RelayCommand ConvertCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand ChooseOutputFolderCommand { get; }
    public RelayCommand ReinstallCommand { get; }

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
                OnPropertyChanged(nameof(OutputFolderDisplay));
        }
    }

    public string OutputFolderDisplay => string.IsNullOrEmpty(OutputFolder)
        ? "Next to each original file"
        : OutputFolder;

    public bool CanConvert => IsReady && !IsBusy && Files.Count > 0;

    /// <summary>Offer the "Reinstall" action when the environment isn't ready (missing/broken) and we're idle.</summary>
    public bool ShowReinstall => !IsReady && !IsBusy;

    private bool HasConvertedFiles => Files.Any(f => f.IsDone);

    /// <summary>
    /// Runs once at startup: prepares or auto-updates MarkItDown to the validated version.
    /// </summary>
    public Task InitializeAsync() => PrepareEnvironmentAsync(forceReinstall: false);

    /// <summary>Clean reinstall of MdPipe's private Python + MarkItDown (triggered by the "Reinstall" button).</summary>
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
            // Setup ran but couldn't finish (usually the first-time download). If a working MarkItDown is
            // already installed, just carry on; otherwise show what actually went wrong.
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
        foreach (var path in paths.Where(File.Exists))
        {
            if (existing.Add(path))
                Files.Add(new FileItemViewModel(path));
        }
    }

    private async Task ConvertAllAsync()
    {
        IsBusy = true;
        StatusMessage = "Converting files…";
        try
        {
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
                    var result = await Task.Run(() => _converter.ConvertAsync(request));

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
                catch (Exception ex)
                {
                    // A single bad file must never abort the batch or crash the app.
                    file.ErrorMessage = ex.Message;
                    file.Status = FileStatus.Error;
                }
            }

            StatusMessage = converted == pending.Count
                ? $"Done · {converted} file(s) converted"
                : $"Finished with warnings · {converted}/{pending.Count} converted";
        }
        finally
        {
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
