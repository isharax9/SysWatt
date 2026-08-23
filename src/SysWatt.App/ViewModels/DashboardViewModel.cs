using System.Windows;
using SysWatt.Core.History;
using SysWatt.Core.Monitoring;
using SysWatt.Core.Sensors;

namespace SysWatt.App.ViewModels;

public sealed record FanDisplayItem(string Name, string Hardware, string Category, string Value, string Details);

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
    public string CpuTemperatureStatus => Status(MetricKind.CpuTemperature);
    public string CpuPowerStatus => Status(MetricKind.CpuPower);
    public string GpuUsage => Format(MetricKind.GpuUsage, "0", "%");
    public string GpuTemperature => Format(MetricKind.GpuTemperature, "0", "°C");
    public string GpuPower => Format(MetricKind.GpuPower, "0", "W");
    public string MemoryUsage => Format(MetricKind.MemoryUsage, "0", "%");
    public string StorageActivity => Format(MetricKind.StorageActivity, "0", "%");
    public string FanSpeed => Format(MetricKind.FanSpeed, "0", " RPM");
    public IReadOnlyList<FanDisplayItem> Fans => _snapshot.Fans
        .Select(fan => new FanDisplayItem(
            fan.SensorName,
            fan.HardwareName,
            FanCategory(fan.HardwareKind),
            $"{fan.Rpm:0} RPM",
            fan.Explanation))
        .ToArray();
    public string FanSummary => _snapshot.Fans.Count == 0
        ? "No fan RPM sensors exposed"
        : $"{_snapshot.Fans.Count} live RPM sensor{(_snapshot.Fans.Count == 1 ? string.Empty : "s")}";
    public string SensorAccessNotice
    {
        get
        {
            var hwinfoGuidance = _monitoring.LastRawReadings.FirstOrDefault(reading =>
                reading.Descriptor.Provider.Equals("HWiNFO Shared Memory", StringComparison.OrdinalIgnoreCase) &&
                !reading.IsAvailable &&
                !string.IsNullOrWhiteSpace(reading.Error));
            var missing = new List<string>();
            if (!_snapshot[MetricKind.CpuTemperature].IsAvailable) missing.Add("CPU temperature");
            if (!_snapshot[MetricKind.CpuPower].IsAvailable) missing.Add("CPU power");
            if (_snapshot.Fans.Count == 0) missing.Add("fan RPM");
            if (hwinfoGuidance is not null) return hwinfoGuidance.Error!;
            return missing.Count == 0
                ? string.Empty
                : $"Unavailable: {string.Join(", ", missing)}. Hardware/firmware support or low-level sensor permissions may be required; hover an N/A value for details.";
        }
    }
    public Visibility SensorAccessVisibility => string.IsNullOrEmpty(SensorAccessNotice) ? Visibility.Collapsed : Visibility.Visible;
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

    private string Status(MetricKind kind)
    {
        var reading = _snapshot[kind];
        if (!reading.Value.HasValue) return reading.Explanation ?? "No compatible hardware sensor was exposed.";
        return string.IsNullOrWhiteSpace(reading.SourceName)
            ? reading.Explanation ?? "Live hardware reading"
            : $"{reading.SourceName}\n{reading.Explanation}";
    }

    private static string FanCategory(HardwareKind kind) => kind switch
    {
        HardwareKind.Cpu => "CPU",
        HardwareKind.GpuNvidia or HardwareKind.GpuAmd or HardwareKind.GpuIntel => "GPU",
        HardwareKind.Motherboard or HardwareKind.Controller => "SYSTEM",
        _ => "FAN"
    };

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
