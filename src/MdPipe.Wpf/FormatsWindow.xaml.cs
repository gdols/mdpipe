using System.Windows;
using MdPipe.Core.Models;
using MdPipe.Wpf.Resources;

namespace MdPipe.Wpf;

public partial class FormatsWindow : Window
{
    public FormatsWindow(FormatCatalog catalog)
    {
        InitializeComponent();

        // Saying where the list came from matters: before the first setup finishes it is the list
        // MdPipe ships with, and promising formats the machine hasn't installed yet would be a lie.
        SourceLine.Text = catalog.IsBaseline
            ? Strings.FormatsFromBundle
            : string.Format(Strings.FormatsFromEngine, catalog.EngineVersion);

        Extensions.ItemsSource = catalog.Extensions;
        CountLine.Text = string.Format(Strings.FormatsCount, catalog.Extensions.Count);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
