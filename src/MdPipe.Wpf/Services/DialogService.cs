using System.Diagnostics;
using System.IO;
using System.Windows;
using MdPipe.Wpf.Resources;

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

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <remarks>
    /// Handing a URL to Windows can fail, and this is called straight from a command handler, where
    /// an exception has nowhere to go but the crash dialog.
    /// </remarks>
    public void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // At least leave them the address they were meant to be taken to.
            ShowMessage(url, Strings.OpenLinkFailedTitle, DialogKind.Information);
        }
    }

    /// <remarks>
    /// The executable has already been replaced by the time this runs. If the new one will not
    /// start, the update is still good on disk, so say so rather than shutting down into nothing.
    /// </remarks>
    public void RestartWith(string executablePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            ShowMessage(Strings.RestartFailedBody, Strings.RestartFailedTitle, DialogKind.Warning);
            return;
        }

        Application.Current.Shutdown();
    }

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
            // Not worth interrupting anyone over.
        }
    }
}
