using System.Windows;
using System.Windows.Input;
using MdPipe.Wpf.Resources;
using MdPipe.Wpf.ViewModels;

namespace MdPipe.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        DropZone.Opacity = 1;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropZone.Opacity = 0.85;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.Opacity = 0.85;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            ViewModel?.AddFiles(paths);
    }

    private void DropZone_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.ChooseFilesTitle,
            Multiselect = true,
            // Built from what the engine reports rather than typed out: the hand-written version was
            // still offering .doc and .ppt, which stopped converting long ago.
            Filter = BuildFilter()
        };

        if (dialog.ShowDialog() == true)
            ViewModel?.AddFiles(dialog.FileNames);
    }

    private string BuildFilter()
    {
        var extensions = ViewModel?.Formats.Extensions ?? [];
        if (extensions.Count == 0) return $"{Strings.AllFiles}|*.*";

        var patterns = string.Join(";", extensions.Select(e => "*" + e));
        return $"{Strings.SupportedDocuments}|{patterns}|{Strings.AllFiles}|*.*";
    }

    private void FormatsLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        new FormatsWindow(ViewModel.Formats) { Owner = this }.ShowDialog();
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }
}
