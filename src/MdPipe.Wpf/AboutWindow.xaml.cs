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
        // Same as the update link: a machine with no browser registered must not crash the app.
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
