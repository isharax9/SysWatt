using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using SysWatt.App.ViewModels;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace SysWatt.App.Views;

public partial class DashboardWindow : Window
{
    private bool _allowClose;
    public event EventHandler? SettingsRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler? RestartElevatedRequested;

    public DashboardWindow() => InitializeComponent();

    public void ToggleDashboard()
    {
        if (IsVisible) { Hide(); return; }
        ShowDashboard();
    }

    public void ShowDashboard()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void CloseForExit() { _allowClose = true; Close(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void Customize_Click(object sender, RoutedEventArgs e) => CustomizePopup.IsOpen = !CustomizePopup.IsOpen;
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void About_Click(object sender, RoutedEventArgs e) => AboutRequested?.Invoke(this, EventArgs.Empty);
    private void RestartElevated_Click(object sender, RoutedEventArgs e) => RestartElevatedRequested?.Invoke(this, EventArgs.Empty);

    private async void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel viewModel) await viewModel.ToggleThemeAsync();
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string target }) return;
        FrameworkElement section = target switch
        {
            "Performance" => PerformanceSection,
            "Storage" => StorageSection,
            "Cooling" => CoolingSection,
            "Energy" => EnergySection,
            _ => DashboardSection
        };
        if (DataContext is DashboardViewModel vm)
        {
            if (target == "Performance") vm.ShowPerformanceCharts = true;
            else if (target == "Storage") vm.ShowStorage = true;
            else if (target == "Cooling") vm.ShowCooling = true;
            else if (target == "Energy") vm.ShowEnergy = true;
            else vm.ShowSystemOverview = true;
        }
        foreach (var button in new[] { DashboardNav, PerformanceNav, StorageNav, CoolingNav, EnergyNav }) button.Tag = null;
        ((Button)sender).Tag = "Selected";
        Dispatcher.BeginInvoke(() => section.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void ExportEnergy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export measured SysWatt energy history",
            Filter = "SysWatt energy archive (*.syswatt-energy.json)|*.syswatt-energy.json|JSON files (*.json)|*.json",
            FileName = $"syswatt-energy-{DateTime.Now:yyyyMMdd}.syswatt-energy.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await vm.ExportEnergyAsync(dialog.FileName);
            MessageBox.Show(this, "Energy history was exported successfully.", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, $"Energy history could not be exported.\n\n{ex.Message}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void ImportEnergy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import SysWatt energy history",
            Filter = "SysWatt energy archive (*.syswatt-energy.json;*.json)|*.syswatt-energy.json;*.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "Matching calendar dates will be replaced by the imported measured totals. Continue?", "Import energy history", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var count = await vm.ImportEnergyAsync(dialog.FileName);
            MessageBox.Show(this, $"Imported {count} daily energy record{(count == 1 ? string.Empty : "s")}.", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, $"Energy history could not be imported.\n\n{ex.Message}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
