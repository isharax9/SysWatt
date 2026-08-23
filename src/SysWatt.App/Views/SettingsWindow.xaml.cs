using System.Windows;
using System.Windows.Input;
using SysWatt.App.ViewModels;

namespace SysWatt.App.Views;

public partial class SettingsWindow : Window
{
    public event EventHandler? ExportDiagnosticsRequested;
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e) => ExportDiagnosticsRequested?.Invoke(this, EventArgs.Empty);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
