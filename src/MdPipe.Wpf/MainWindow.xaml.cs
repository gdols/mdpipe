using System.Windows;
using System.Windows.Input;
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
            Title = "Choose the files to convert",
            Multiselect = true,
            Filter = "Supported documents|*.pdf;*.docx;*.doc;*.pptx;*.ppt;*.xlsx;*.xls;*.html;*.htm;*.csv;*.json;*.xml;*.txt;*.png;*.jpg;*.jpeg|All files|*.*"
        };

        if (dialog.ShowDialog() == true)
            ViewModel?.AddFiles(dialog.FileNames);
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }
}
