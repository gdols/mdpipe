using System.IO;
using MdPipe.Wpf.Mvvm;

namespace MdPipe.Wpf.ViewModels;

public enum FileStatus
{
    Pending,
    Converting,
    Done,
    Error
}

public sealed class FileItemViewModel(string sourcePath) : ObservableObject
{
    private FileStatus _status = FileStatus.Pending;
    private string? _outputPath;
    private string? _errorMessage;

    public string SourcePath { get; } = sourcePath;
    public string FileName => Path.GetFileName(SourcePath);
    public string Folder => Path.GetDirectoryName(SourcePath) ?? string.Empty;

    public FileStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsDone));
            }
        }
    }

    public string? OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsDone => Status == FileStatus.Done;

    public string StatusText => Status switch
    {
        FileStatus.Pending => "Pending",
        FileStatus.Converting => "Converting…",
        FileStatus.Done => "Converted",
        FileStatus.Error => $"Error: {ErrorMessage}",
        _ => string.Empty
    };
}
