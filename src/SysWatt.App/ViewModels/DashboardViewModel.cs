using System.Windows;
using SysWatt.Core.History;
using SysWatt.Core.Monitoring;
using SysWatt.Core.Sensors;

namespace SysWatt.App.ViewModels;

public sealed class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IMonitoringService _monitoring;
    private MetricSnapshot _snapshot;
    private string? _activeAlert;

    public DashboardViewModel(IMonitoringService monitoring)
    {
        _monitoring = monitoring;
        _snapshot = monitoring.Current;
        monitoring.SnapshotUpdated += OnSnapshotUpdated;
        monitoring.AlertTriggered += OnAlertTriggered;
    }

    public string UpdatedText => $"UPDATED {_snapshot.Timestamp.ToLocalTime():HH:mm:ss}";
    public string CpuUsage => Format(MetricKind.CpuUsage, "0", "%");
    public string CpuTemperature => Format(MetricKind.CpuTemperature, "0", "°C");
    public string CpuPower => Format(MetricKind.CpuPower, "0", "W");
    public string GpuUsage => Format(MetricKind.GpuUsage, "0", "%");
    public string GpuTemperature => Format(MetricKind.GpuTemperature, "0", "°C");
    public string GpuPower => Format(MetricKind.GpuPower, "0", "W");
    public string MemoryUsage => Format(MetricKind.MemoryUsage, "0", "%");
    public string StorageActivity => Format(MetricKind.StorageActivity, "0", "%");
    public string FanSpeed => Format(MetricKind.FanSpeed, "0", " RPM");
    public string EstimatedDcPower => Format(MetricKind.EstimatedDcPower, "0", " W");
    public string EstimatedWallPower => Format(MetricKind.EstimatedWallPower, "0", " W");
    public string EstimateStatus => _snapshot[MetricKind.EstimatedWallPower].Explanation ?? "Waiting for first sample…";
    public string? ActiveAlert { get => _activeAlert; private set => Set(ref _activeAlert, value); }
    public Visibility AlertVisibility => string.IsNullOrEmpty(ActiveAlert) ? Visibility.Collapsed : Visibility.Visible;
    public IReadOnlyList<HistoryPoint> CpuUsageHistory => _monitoring.History.Get(MetricKind.CpuUsage);
    public IReadOnlyList<HistoryPoint> CpuTemperatureHistory => _monitoring.History.Get(MetricKind.CpuTemperature);
    public IReadOnlyList<HistoryPoint> GpuUsageHistory => _monitoring.History.Get(MetricKind.GpuUsage);
    public IReadOnlyList<HistoryPoint> GpuTemperatureHistory => _monitoring.History.Get(MetricKind.GpuTemperature);
    public IReadOnlyList<HistoryPoint> PowerHistory => _monitoring.History.Get(MetricKind.EstimatedWallPower);

    private string Format(MetricKind kind, string format, string suffix)
    {
        var value = _snapshot.Value(kind);
        return value.HasValue ? value.Value.ToString(format) + suffix : "N/A";
    }

    private void OnSnapshotUpdated(object? sender, MetricSnapshot snapshot)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _snapshot = snapshot;
            foreach (var property in typeof(DashboardViewModel).GetProperties().Where(p => p.Name != nameof(ActiveAlert)))
                OnPropertyChanged(property.Name);
        });
    }

    private void OnAlertTriggered(object? sender, SysWatt.Core.Alerts.AlertEvent alert)
    {
        if (!alert.Rule.ShowInApp) return;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            ActiveAlert = alert.Message;
            OnPropertyChanged(nameof(AlertVisibility));
        });
    }

    public void Dispose()
    {
        _monitoring.SnapshotUpdated -= OnSnapshotUpdated;
        _monitoring.AlertTriggered -= OnAlertTriggered;
    }
}
