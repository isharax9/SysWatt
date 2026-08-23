using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SysWatt.Core.Alerts;
using SysWatt.Core.Monitoring;
using SysWatt.Core.Sensors;
using SysWatt.Core.Settings;

namespace SysWatt.App.Windows;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly IMonitoringService _monitoring;
    private readonly ToolStripMenuItem _startupItem;
    private Icon? _renderedIcon;
    private AppSettings _settings;

    public event EventHandler? QuickDashboardRequested;
    public event EventHandler? MainDashboardRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? StartupChanged;

    public TrayIconService(IMonitoringService monitoring, AppSettings settings)
    {
        _monitoring = monitoring;
        _settings = settings;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Quick Dashboard", null, (_, _) => QuickDashboardRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Open Full Dashboard", null, (_, _) => MainDashboardRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _startupItem = new ToolStripMenuItem("Start with Windows") { Checked = settings.StartWithWindows, CheckOnClick = true };
        _startupItem.CheckedChanged += (_, _) => StartupChanged?.Invoke(this, _startupItem.Checked);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _notifyIcon = new NotifyIcon { ContextMenuStrip = menu, Text = "SysWatt · waiting for sensors", Visible = true };
        SetBrandIcon();
        _notifyIcon.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) QuickDashboardRequested?.Invoke(this, EventArgs.Empty); };
        monitoring.SnapshotUpdated += OnSnapshotUpdated;
        monitoring.AlertTriggered += OnAlertTriggered;
        monitoring.TelemetryModeChanged += OnTelemetryModeChanged;
    }

    private void SetBrandIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            var icon = executable is null ? null : Icon.ExtractAssociatedIcon(executable);
            if (icon is null) { UpdateIcon(null, "SW"); return; }
            _renderedIcon = icon;
            _notifyIcon.Icon = icon;
        }
        catch { UpdateIcon(null, "SW"); }
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _startupItem.Checked = settings.StartWithWindows;
        OnSnapshotUpdated(this, _monitoring.Current);
    }

    private void OnSnapshotUpdated(object? sender, MetricSnapshot snapshot)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var metric = _settings.TrayMetric;
            var value = snapshot.Value(metric);
            var text = value.HasValue ? IconText(metric, value.Value) : "N/A";
            UpdateIcon(value, text);
            var cpu = Compact(snapshot, MetricKind.CpuTemperature);
            var gpu = Compact(snapshot, MetricKind.GpuTemperature);
            var watts = Compact(snapshot, MetricKind.EstimatedWallPower);
            var source = snapshot.Source switch
            {
                TelemetrySource.HWiNFOBridge => "HWiNFO",
                TelemetrySource.FullHardwareAccess => "Hardware",
                _ => "Standalone"
            };
            _notifyIcon.Text = Truncate($"SysWatt · {source} · Wall {watts} · CPU {cpu} · GPU {gpu}", 63);
        });
    }

    private void OnAlertTriggered(object? sender, AlertEvent alert)
    {
        if (!alert.Rule.ShowDesktopNotification) return;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _notifyIcon.ShowBalloonTip(5000, $"SysWatt · {alert.Rule.Severity}", alert.Message, ToolTipIcon.Warning));
    }

    private void OnTelemetryModeChanged(object? sender, SysWatt.Core.Monitoring.TelemetryModeChangedEventArgs change)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _notifyIcon.ShowBalloonTip(5000, "SysWatt telemetry source", change.Message, change.Current == TelemetrySource.Standalone
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info));
    }

    private static string Compact(MetricSnapshot snapshot, MetricKind metric) =>
        snapshot.Value(metric) is { } value ? $"{value:0}{MetricUnits.For(metric)}" : "N/A";

    private static string IconText(MetricKind metric, double value) => metric switch
    {
        MetricKind.CpuTemperature or MetricKind.GpuTemperature => $"{value:0}°",
        MetricKind.CpuUsage or MetricKind.GpuUsage => $"{value:0}",
        _ => value >= 1000 ? $"{value / 1000:0.0}k" : $"{value:0}"
    };

    private void UpdateIcon(double? value, string text)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.FromArgb(16, 20, 24));
            var accent = value.HasValue ? Color.FromArgb(118, 230, 180) : Color.FromArgb(132, 145, 155);
            using var pen = new Pen(accent, 2);
            graphics.DrawRectangle(pen, 1.5f, 1.5f, 29, 29);
            var fontSize = text.Length switch { <= 2 => 15f, 3 => 12f, _ => 9f };
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            var size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, brush, (32 - size.Width) / 2, (32 - size.Height) / 2);
        }
        var handle = bitmap.GetHicon();
        try
        {
            var next = (Icon)Icon.FromHandle(handle).Clone();
            _notifyIcon.Icon = next;
            _renderedIcon?.Dispose();
            _renderedIcon = next;
        }
        finally { DestroyIcon(handle); }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _monitoring.SnapshotUpdated -= OnSnapshotUpdated;
        _monitoring.AlertTriggered -= OnAlertTriggered;
        _monitoring.TelemetryModeChanged -= OnTelemetryModeChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _renderedIcon?.Dispose();
    }
}
