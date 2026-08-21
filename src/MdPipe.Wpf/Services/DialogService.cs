using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MdPipe.Wpf.Services;

/// <summary>The real thing: WPF message boxes, the Windows folder picker and Explorer.</summary>
public sealed class DialogService : IDialogService
{
    public void ShowMessage(string message, string title, DialogKind kind) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, kind switch
        {
            DialogKind.Error => MessageBoxImage.Error,
            DialogKind.Warning => MessageBoxImage.Warning,
            _ => MessageBoxImage.Information
        });

    public string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            // Not being able to open Explorer is not worth interrupting anyone over.
        }
    }
}
