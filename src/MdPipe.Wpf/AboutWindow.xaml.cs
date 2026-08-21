using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using MdPipe.Wpf.Resources;

namespace MdPipe.Wpf;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null
            ? ""
            : string.Format(Strings.VersionLabel, $"{version.Major}.{version.Minor}.{version.Build}");
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
