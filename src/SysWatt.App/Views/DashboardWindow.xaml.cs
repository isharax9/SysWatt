using System.ComponentModel;
using System.Windows;
using SysWatt.App.Theming;
using SysWatt.App.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace SysWatt.App.Views;

public partial class DashboardWindow : Window
{
    private bool _allowClose;
    public event EventHandler? SettingsRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler? RestartElevatedRequested;
    public event EventHandler? EnergyHistoryRequested;

    public DashboardWindow()
    {
        InitializeComponent();
        ThemeManager.ApplyToWindow(this);
        StateChanged += (_, _) => UpdateIsActive();
        IsVisibleChanged += (_, _) => UpdateIsActive();
    }

    public void ToggleDashboard()
    {
        if (IsVisible && WindowState != WindowState.Minimized) { Hide(); return; }
        ShowDashboard();
    }

    public void ShowDashboard()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        UpdateIsActive();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            UpdateIsActive();
            return;
        }
        base.OnClosing(e);
    }

    private void UpdateIsActive()
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.IsActive = IsVisible && WindowState != WindowState.Minimized;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void About_Click(object sender, RoutedEventArgs e) => AboutRequested?.Invoke(this, EventArgs.Empty);
    private void EnergyHistory_Click(object sender, RoutedEventArgs e) => EnergyHistoryRequested?.Invoke(this, EventArgs.Empty);
    private void RestartElevated_Click(object sender, RoutedEventArgs e) => RestartElevatedRequested?.Invoke(this, EventArgs.Empty);

    private async void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel viewModel) await viewModel.ToggleThemeAsync();
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
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Energy history could not be exported.\n\n{ex.Message}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Energy history could not be imported.\n\n{ex.Message}", "SysWatt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
