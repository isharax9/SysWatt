using System.Windows;
using SysWatt.Core.Energy;
using SysWatt.Core.History;
using SysWatt.Core.Monitoring;
using SysWatt.Core.Sensors;
using SysWatt.Core.Settings;

namespace SysWatt.App.ViewModels;

public sealed record FanDisplayItem(string Name, string Hardware, string Category, string Value, double Rpm, double GaugePercent, string Details);

public sealed class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IMonitoringService _monitoring;
    private readonly ISettingsStore _settingsStore;
    private MetricSnapshot _snapshot;
    private AppSettings _settings;
    private string? _activeAlert;
    private DateTime? _selectedEnergyDate = DateTime.Today;
    private DailyEnergySummary _selectedDay = new(DateOnly.FromDateTime(DateTime.Today), 0, 0, 0);
    private double _todayKwh;
    private double _weekKwh;
    private double _monthKwh;
    private DateTimeOffset _lastEnergyRefresh;
    private IReadOnlyList<HistoryPoint> _dailyEnergyHistory = [];

    public event EventHandler<AppSettings>? SettingsChanged;

    public DashboardViewModel(IMonitoringService monitoring, ISettingsStore settingsStore, AppSettings settings)
    {
        _monitoring = monitoring;
        _settingsStore = settingsStore;
        _settings = settings;
        _snapshot = monitoring.Current;
        monitoring.SnapshotUpdated += OnSnapshotUpdated;
        monitoring.AlertTriggered += OnAlertTriggered;
        _ = RefreshEnergyAsync();
    }

    public string UpdatedText => $"LIVE · {_snapshot.Timestamp.ToLocalTime():HH:mm:ss}";
    public string CpuUsage => Format(MetricKind.CpuUsage, "0", "%");
    public string CpuTemperature => Format(MetricKind.CpuTemperature, "0", "°C");
    public string CpuPower => FormatPower(MetricKind.CpuPower);
    public string CpuPowerSource => SourceLabel(MetricKind.CpuPower);
    public string CpuTemperatureStatus => Status(MetricKind.CpuTemperature);
    public string CpuPowerStatus => Status(MetricKind.CpuPower);
    public string GpuUsage => Format(MetricKind.GpuUsage, "0", "%");
    public string GpuTemperature => Format(MetricKind.GpuTemperature, "0", "°C");
    public string GpuPower => FormatPower(MetricKind.GpuPower);
    public string GpuPowerSource => SourceLabel(MetricKind.GpuPower);
    public string MemoryUsage => Format(MetricKind.MemoryUsage, "0", "%");
    public string StorageActivity => Format(MetricKind.StorageActivity, "0", "%");
    public string StorageReadRate => Format(MetricKind.StorageReadRate, "0.0", " MB/s");
    public string StorageWriteRate => Format(MetricKind.StorageWriteRate, "0.0", " MB/s");
    public string StoragePower => Format(MetricKind.StoragePower, "0.0", " W");
    public string StorageTemperature => Format(MetricKind.StorageTemperature, "0", "°C");
    public IReadOnlyList<FanDisplayItem> Fans => _snapshot.Fans
        .Select(fan => new FanDisplayItem(
            fan.SensorName, fan.HardwareName, FanCategory(fan.HardwareKind), $"{fan.Rpm:0} RPM", fan.Rpm,
            Math.Clamp(fan.Rpm / 30d, 0, 100), fan.Explanation))
        .ToArray();
    public string FanSummary => _snapshot.Fans.Count == 0
        ? "No RPM header exposed by this board"
        : $"{_snapshot.Fans.Count} live channel{(_snapshot.Fans.Count == 1 ? string.Empty : "s")}";
    public string SensorAccessNotice
    {
        get
        {
            var lowLevelAccess = _monitoring.LastRawReadings.FirstOrDefault(reading =>
                reading.Descriptor.SensorId.Equals("/librehardwaremonitor/pawnio-missing", StringComparison.OrdinalIgnoreCase));
            var missing = new List<string>();
            if (!_snapshot[MetricKind.CpuTemperature].IsAvailable) missing.Add("CPU temperature");
            if (!_snapshot[MetricKind.GpuTemperature].IsAvailable) missing.Add("GPU temperature");
            if (_snapshot.Fans.Count == 0) missing.Add("fan RPM");
            if (lowLevelAccess is not null && missing.Any(item => item.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)))
                return $"{lowLevelAccess.Error} Modeled CPU watts are shown with a ~ prefix and should be calibrated against a trusted package-power reading.";
            return missing.Count == 0
                ? string.Empty
                : $"Native collection is active. Your firmware does not expose: {string.Join(", ", missing)}. Power still uses the SysWatt utilization model; temperature and RPM cannot be inferred safely.";
        }
    }
    public Visibility SensorAccessVisibility => string.IsNullOrEmpty(SensorAccessNotice) ? Visibility.Collapsed : Visibility.Visible;
    public string EstimatedDcPower => Format(MetricKind.EstimatedDcPower, "0", " W");
    public string EstimatedWallPower => Format(MetricKind.EstimatedWallPower, "0", " W");
    public string EstimateStatus => _snapshot[MetricKind.EstimatedWallPower].Explanation ?? "Waiting for the first sample…";
    public string? ActiveAlert { get => _activeAlert; private set => Set(ref _activeAlert, value); }
    public Visibility AlertVisibility => string.IsNullOrEmpty(ActiveAlert) ? Visibility.Collapsed : Visibility.Visible;
    public IReadOnlyList<HistoryPoint> CpuUsageHistory => _monitoring.History.Get(MetricKind.CpuUsage);
    public IReadOnlyList<HistoryPoint> CpuTemperatureHistory => _monitoring.History.Get(MetricKind.CpuTemperature);
    public IReadOnlyList<HistoryPoint> GpuUsageHistory => _monitoring.History.Get(MetricKind.GpuUsage);
    public IReadOnlyList<HistoryPoint> GpuTemperatureHistory => _monitoring.History.Get(MetricKind.GpuTemperature);
    public IReadOnlyList<HistoryPoint> StorageActivityHistory => _monitoring.History.Get(MetricKind.StorageActivity);
    public IReadOnlyList<HistoryPoint> PowerHistory => _monitoring.History.Get(MetricKind.EstimatedWallPower);

    public string TodayEnergy => $"{_todayKwh:0.000} kWh";
    public string WeekEnergy => $"{_weekKwh:0.00} kWh";
    public string MonthEnergy => $"{_monthKwh:0.00} kWh";
    public string SelectedDayEnergy => $"{_selectedDay.KilowattHours:0.000} kWh";
    public string SelectedDayDetails => $"Average {_selectedDay.AverageWatts:0} W · peak {_selectedDay.PeakWatts:0} W";
    public IReadOnlyList<HistoryPoint> DailyEnergyHistory => _dailyEnergyHistory;
    public DateTime? SelectedEnergyDate
    {
        get => _selectedEnergyDate;
        set
        {
            if (!Set(ref _selectedEnergyDate, value) || !value.HasValue) return;
            _ = RefreshSelectedDayAsync(DateOnly.FromDateTime(value.Value));
        }
    }

    public bool ShowSystemOverview { get => _settings.Dashboard.ShowSystemOverview; set => UpdateLayout(_settings.Dashboard with { ShowSystemOverview = value }); }
    public bool ShowPerformanceCharts { get => _settings.Dashboard.ShowPerformanceCharts; set => UpdateLayout(_settings.Dashboard with { ShowPerformanceCharts = value }); }
    public bool ShowStorage { get => _settings.Dashboard.ShowStorage; set => UpdateLayout(_settings.Dashboard with { ShowStorage = value }); }
    public bool ShowCooling { get => _settings.Dashboard.ShowCooling; set => UpdateLayout(_settings.Dashboard with { ShowCooling = value }); }
    public bool ShowEnergy { get => _settings.Dashboard.ShowEnergy; set => UpdateLayout(_settings.Dashboard with { ShowEnergy = value }); }
    public bool TrayPopupPinned
    {
        get => _settings.TrayDashboardPinned;
        set
        {
            if (_settings.TrayDashboardPinned == value) return;
            _settings = _settings with { TrayDashboardPinned = value };
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }
    public Visibility SystemOverviewVisibility => Visible(ShowSystemOverview);
    public Visibility PerformanceChartsVisibility => Visible(ShowPerformanceCharts);
    public Visibility StorageVisibility => Visible(ShowStorage);
    public Visibility CoolingVisibility => Visible(ShowCooling);
    public Visibility EnergyVisibility => Visible(ShowEnergy);

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        RaiseLayoutProperties();
    }

    private string Format(MetricKind kind, string format, string suffix)
    {
        var value = _snapshot.Value(kind);
        return value.HasValue ? value.Value.ToString(format) + suffix : "N/A";
    }

    private string FormatPower(MetricKind kind)
    {
        var reading = _snapshot[kind];
        if (!reading.IsAvailable || !reading.Value.HasValue) return "N/A";
        var modeled = reading.SourceName?.Contains("model", StringComparison.OrdinalIgnoreCase) == true;
        return $"{(modeled ? "~" : string.Empty)}{reading.Value.Value:0} W";
    }

    private string SourceLabel(MetricKind kind) => _snapshot[kind].SourceName?.Contains("model", StringComparison.OrdinalIgnoreCase) == true
        ? "ESTIMATED · UTILIZATION MODEL"
        : "HARDWARE SENSOR";

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
        _ => "COOLING"
    };

    private void OnSnapshotUpdated(object? sender, MetricSnapshot snapshot)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _snapshot = snapshot;
            foreach (var property in typeof(DashboardViewModel).GetProperties().Where(p => p.Name != nameof(ActiveAlert)))
                OnPropertyChanged(property.Name);
            if (DateTimeOffset.UtcNow - _lastEnergyRefresh > TimeSpan.FromSeconds(15)) _ = RefreshEnergyAsync();
        });
    }

    private async Task RefreshEnergyAsync()
    {
        try
        {
            _lastEnergyRefresh = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var range = await _monitoring.EnergyHistory.GetRangeAsync(today.AddDays(-29), today);
            var todayRow = range[^1];
            _todayKwh = todayRow.KilowattHours;
            _weekKwh = range.TakeLast(7).Sum(x => x.KilowattHours);
            _monthKwh = range.Where(x => x.Date >= monthStart).Sum(x => x.KilowattHours);
            _dailyEnergyHistory = range.Select(x => new HistoryPoint(x.Date.ToDateTime(new TimeOnly(12, 0)), x.KilowattHours)).ToArray();
            var selected = SelectedEnergyDate.HasValue ? DateOnly.FromDateTime(SelectedEnergyDate.Value) : today;
            _selectedDay = selected == today ? todayRow : await _monitoring.EnergyHistory.GetDayAsync(selected);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(TodayEnergy)); OnPropertyChanged(nameof(WeekEnergy)); OnPropertyChanged(nameof(MonthEnergy));
                OnPropertyChanged(nameof(SelectedDayEnergy)); OnPropertyChanged(nameof(SelectedDayDetails)); OnPropertyChanged(nameof(DailyEnergyHistory));
            });
        }
        catch { }
    }

    private async Task RefreshSelectedDayAsync(DateOnly date)
    {
        try
        {
            _selectedDay = await _monitoring.EnergyHistory.GetDayAsync(date);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(SelectedDayEnergy)); OnPropertyChanged(nameof(SelectedDayDetails));
            });
        }
        catch { }
    }

    private async void UpdateLayout(DashboardLayoutSettings layout)
    {
        if (_settings.Dashboard == layout) return;
        _settings = _settings with { Dashboard = layout };
        RaiseLayoutProperties();
        try
        {
            await _settingsStore.SaveAsync(_settings);
            SettingsChanged?.Invoke(this, _settings);
        }
        catch { }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
            SettingsChanged?.Invoke(this, _settings);
        }
        catch { }
    }

    private void RaiseLayoutProperties()
    {
        OnPropertyChanged(nameof(ShowSystemOverview)); OnPropertyChanged(nameof(ShowPerformanceCharts));
        OnPropertyChanged(nameof(ShowStorage)); OnPropertyChanged(nameof(ShowCooling)); OnPropertyChanged(nameof(ShowEnergy));
        OnPropertyChanged(nameof(SystemOverviewVisibility)); OnPropertyChanged(nameof(PerformanceChartsVisibility));
        OnPropertyChanged(nameof(StorageVisibility)); OnPropertyChanged(nameof(CoolingVisibility)); OnPropertyChanged(nameof(EnergyVisibility));
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

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
