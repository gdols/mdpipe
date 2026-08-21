using System.Windows;
using MdPipe.Core.Models;

namespace MdPipe.Wpf;

public partial class FormatsWindow : Window
{
    public FormatsWindow(FormatCatalog catalog)
    {
        InitializeComponent();

        // Saying where the list came from matters: before the first setup finishes it is the list
        // MdPipe ships with, and promising formats the machine hasn't installed yet would be a lie.
        SourceLine.Text = catalog.IsBaseline
            ? "The formats MdPipe ships knowing about. Once it finishes setting itself up, this list comes straight from the engine on this computer."
            : $"Read from MarkItDown {catalog.EngineVersion}, the engine installed on this computer.";

        Extensions.ItemsSource = catalog.Extensions;
        CountLine.Text = $"{catalog.Extensions.Count} formats.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
