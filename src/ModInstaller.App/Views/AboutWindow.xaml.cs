using System.Diagnostics;
using System.Windows;

namespace ModInstaller.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Patreon_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.patreon.com/eedevv",
                UseShellExecute = true
            });
        }
        catch { }
    }
}
