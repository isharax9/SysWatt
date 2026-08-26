using System.Windows;
using SysWatt.App.Theming;
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
        ThemeManager.ApplyToWindow(this);
    }

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e) =>
        ExportDiagnosticsRequested?.Invoke(this, EventArgs.Empty);
}
