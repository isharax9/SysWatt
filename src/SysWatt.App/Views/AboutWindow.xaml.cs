using System.Diagnostics;
using System.Reflection;
using System.Windows;
using SysWatt.App.Theming;

namespace SysWatt.App.Views;

public partial class AboutWindow : Window
{
    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
        ThemeManager.ApplyToWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Project_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/isharax9/PerfMetrics");

    private void ReportBug_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/isharax9/PerfMetrics/issues/new?title=%5BBug%5D+&labels=bug");

    private void Url_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            OpenUrl(url);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
