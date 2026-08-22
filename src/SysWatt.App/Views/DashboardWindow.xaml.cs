using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

namespace SysWatt.App.Views;

public partial class DashboardWindow : Window
{
    private bool _allowClose;
    public event EventHandler? SettingsRequested;

    public DashboardWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => { if (IsVisible && !IsMouseOver) Hide(); };
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
        Left = (work.Right / dpi.DpiScaleX) - Width - 10;
        Top = (work.Bottom / dpi.DpiScaleY) - Height - 10;
        Show(); Activate();
    }

    public void CloseForExit() { _allowClose = true; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
}
