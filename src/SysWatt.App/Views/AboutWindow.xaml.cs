using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace SysWatt.App.Views;

public partial class AboutWindow : Window
{
    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Project_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
    {
        FileName = "https://github.com/isharax9/PerfMetrics",
        UseShellExecute = true
    });
}
