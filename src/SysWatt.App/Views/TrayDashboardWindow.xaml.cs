using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using SysWatt.App.ViewModels;

namespace SysWatt.App.Views;

public partial class TrayDashboardWindow : Window
{
    private bool _allowClose;
    public event EventHandler? OpenFullDashboardRequested;
    public event EventHandler? OpenSettingsRequested;

    public TrayDashboardWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (IsVisible && DataContext is DashboardViewModel { TrayPopupPinned: false } && !IsMouseOver) Hide();
        };
    }

    public void ToggleNearTray()
    {
        if (IsVisible) { Hide(); return; }
        ShowNearTray();
    }

    public void ShowNearTray()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var work = Screen.FromPoint(cursor).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = work.Right / dpi.DpiScaleX - Width - 10;
        Top = work.Bottom / dpi.DpiScaleY - Height - 10;
        Show(); Activate();
    }

    public void CloseForExit() { _allowClose = true; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel { TrayPopupPinned: false }) Hide();
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OpenFull_Click(object sender, RoutedEventArgs e) { Hide(); OpenFullDashboardRequested?.Invoke(this, EventArgs.Empty); }
}
