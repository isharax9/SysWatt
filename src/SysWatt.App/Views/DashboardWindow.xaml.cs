using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace SysWatt.App.Views;

public partial class DashboardWindow : Window
{
    private bool _allowClose;
    public event EventHandler? SettingsRequested;

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
}
