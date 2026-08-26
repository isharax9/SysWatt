using System.Windows;
using System.Security.Principal;
using SysWatt.App.Commands;
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
    private string? _telemetryNotice;
    private DateTime? _selectedEnergyDate = DateTime.Today;
    private DailyEnergySummary _selectedDay = new(DateOnly.FromDateTime(DateTime.Today), 0, 0, 0);
    private double _todayKwh;
    private double _weekKwh;
    private double _monthKwh;
    private bool _todayHasData;
    private bool _rangeHasData;
    private DateTimeOffset _lastEnergyRefresh;
    private IReadOnlyList<HistoryPoint> _dailyEnergyHistory = [];
    private CancellationTokenSource? _alertDismissal;
    private CancellationTokenSource? _telemetryNoticeDismissal;
    private bool _hideZeroRpmFans;

    public event EventHandler<AppSettings>? SettingsChanged;

    public DashboardViewModel(IMonitoringService monitoring, ISettingsStore settingsStore, AppSettings settings)
    {
        _monitoring = monitoring;
        _settingsStore = settingsStore;
        _settings = settings;
        _snapshot = monitoring.Current;
        monitoring.SnapshotUpdated += OnSnapshotUpdated;
        monitoring.AlertTriggered += OnAlertTriggered;
        monitoring.TelemetryModeChanged += OnTelemetryModeChanged;
        ToggleZeroRpmFansCommand = new(ToggleZeroRpmFans);
        _ = RefreshEnergyAsync();
    }

    public string UpdatedText => $"LIVE · {_snapshot.Timestamp.ToLocalTime():HH:mm:ss}";
    public string TelemetrySourceBadge => _snapshot.Source switch
    {
        TelemetrySource.HWiNFOBridge => "HWiNFO BRIDGE",
        TelemetrySource.FullHardwareAccess => "FULL HARDWARE ACCESS",
        _ => "STANDALONE"
    };
    public string TelemetrySourceDetails => _snapshot.SourceDiagnostic;
    public string TelemetrySourceNotice => _telemetryNotice ?? string.Empty;
    public Visibility TelemetrySourceNoticeVisibility => string.IsNullOrWhiteSpace(_telemetryNotice) ? Visibility.Collapsed : Visibility.Visible;
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
        .Where(fan => !HideZeroRpmFans || fan.Rpm > 0)
        .Select(fan => new FanDisplayItem(
            fan.SensorName, fan.HardwareName, FanCategory(fan.HardwareKind), $"{fan.Rpm:0} RPM", fan.Rpm,
            Math.Clamp(fan.Rpm / 30d, 0, 100), fan.Explanation))
        .ToArray();
    public string FanSummary
    {
        get
        {
            if (_snapshot.Fans.Count == 0) return "No RPM header exposed by this board";
            var active = _snapshot.Fans.Count(fan => fan.Rpm > 0);
            var hidden = _snapshot.Fans.Count - active;
            return HideZeroRpmFans && hidden > 0
                ? $"{active} active · {hidden} hidden"
                : $"{_snapshot.Fans.Count} channel{(_snapshot.Fans.Count == 1 ? string.Empty : "s")}";
        }
    }
    public bool HideZeroRpmFans => _hideZeroRpmFans;
    public string ZeroRpmFanAction => HideZeroRpmFans ? "Show all fans" : "Hide 0 RPM";
    public RelayCommand ToggleZeroRpmFansCommand { get; }
    public bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public string SensorAccessNotice
    {
        get
        {
            var lowLevelAccess = _monitoring.LastRawReadings.FirstOrDefault(reading =>
                reading.Descriptor.SensorId.Equals("/librehardwaremonitor/pawnio-missing", StringComparison.OrdinalIgnoreCase));
            var missing = new List<string>();
            if (!_snapshot[MetricKind.CpuTemperature].IsAvailable) missing.Add("CPU temperature");
            if (!_snapshot[MetricKind.CpuPower].IsAvailable || _snapshot[MetricKind.CpuPower].SourceProvider == "SysWatt calculated model") missing.Add("exact CPU package power");
            if (!_snapshot[MetricKind.GpuTemperature].IsAvailable) missing.Add("GPU temperature");
            if (!_snapshot[MetricKind.GpuPower].IsAvailable || _snapshot[MetricKind.GpuPower].SourceProvider == "SysWatt calculated model") missing.Add("exact GPU board power");
            if (_snapshot.Fans.Count == 0) missing.Add("fan RPM");
            if (missing.Count > 0 && !IsAdministrator)
                return $"Hardware access is restricted in this session. Missing: {string.Join(", ", missing)}. Restart SysWatt as administrator to let the Ryzen/board sensor provider access privileged telemetry. Values remain N/A until a hardware sensor succeeds.";
            if (lowLevelAccess is not null && missing.Any(item => item.Contains("CPU", StringComparison.OrdinalIgnoreCase)))
                return $"{lowLevelAccess.Error} Exact sensor values remain N/A; modeled totals are explicitly marked calculated.";
            return missing.Count == 0
                ? string.Empty
                : $"Full hardware access is active, but this hardware/firmware did not expose: {string.Join(", ", missing)}. Exact readings remain N/A and calculated values are labeled separately.";
        }
    }
    public Visibility SensorAccessVisibility => string.IsNullOrEmpty(SensorAccessNotice) ? Visibility.Collapsed : Visibility.Visible;
    public string SystemPower => FormatPower(MetricKind.EstimatedWallPower);
    public string DcPower => FormatPower(MetricKind.EstimatedDcPower);
    public string SystemPowerStatus => Status(MetricKind.EstimatedWallPower);
    public string StorageCoolingBoardPower
    {
        get
        {
            var values = new[] { _snapshot.Value(MetricKind.StoragePower), _snapshot.Value(MetricKind.CoolingPower), _snapshot.Value(MetricKind.BaseSystemPower) };
            return $"{values.Where(value => value.HasValue).Sum(value => value!.Value):0} W";
        }
    }
    public string MonitorsPeripheralPower => FormatPower(MetricKind.ExternalPower);
    public string MeasuredComponentPower
    {
        get
        {
            var values = new[] { _snapshot.Value(MetricKind.CpuPower), _snapshot.Value(MetricKind.GpuPower), _snapshot.Value(MetricKind.StoragePower) };
            var measured = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            return measured.Length == 0 ? "N/A" : $"{measured.Sum():0} W";
        }
    }
    public string EstimateStatus => SystemPowerStatus;
    public string? ActiveAlert { get => _activeAlert; private set => Set(ref _activeAlert, value); }
    public Visibility AlertVisibility => string.IsNullOrEmpty(ActiveAlert) ? Visibility.Collapsed : Visibility.Visible;
    public IReadOnlyList<HistoryPoint> CpuUsageHistory => Window(MetricKind.CpuUsage);
    public IReadOnlyList<HistoryPoint> CpuPowerHistory => Window(MetricKind.CpuPower);
    public IReadOnlyList<HistoryPoint> CpuTemperatureHistory => Window(MetricKind.CpuTemperature);
    public IReadOnlyList<HistoryPoint> GpuUsageHistory => Window(MetricKind.GpuUsage);
    public IReadOnlyList<HistoryPoint> GpuPowerHistory => Window(MetricKind.GpuPower);
    public IReadOnlyList<HistoryPoint> GpuTemperatureHistory => Window(MetricKind.GpuTemperature);
    public IReadOnlyList<HistoryPoint> StorageActivityHistory => Window(MetricKind.StorageActivity);
    public IReadOnlyList<HistoryPoint> PowerHistory => Window(MetricKind.EstimatedWallPower);
    public string GraphWindowLabel => $"{_settings.GraphWindowMinutes}-minute rolling window · {_settings.PollingIntervalMilliseconds / 1000d:0.#}-second samples";

    public string TodayEnergy => _todayHasData ? $"{_todayKwh:0.000} kWh" : "N/A";
    public string WeekEnergy => _rangeHasData ? $"{_weekKwh:0.000} kWh" : "N/A";
    public string MonthEnergy => _rangeHasData ? $"{_monthKwh:0.000} kWh" : "N/A";
    public string SelectedDayEnergy => _selectedDay.HasData ? $"{_selectedDay.KilowattHours:0.000} kWh" : "N/A";
    public string SelectedDayDetails => _selectedDay.HasData
        ? $"Average {_selectedDay.AverageWatts:0} W · peak {_selectedDay.PeakWatts:0} W\n{_selectedDay.SourceSummary}"
        : "No hybrid wall-power samples were recorded for this day.";
    public string EnergyMeasurementNotice => _snapshot[MetricKind.EstimatedWallPower].IsAvailable
        ? "Energy accumulation is active from the hybrid wall-power model. Exact CPU/GPU sensors are preferred; detected and manual loads are labeled calculated."
        : "Energy accumulation is waiting for a valid wall-power result.";
    public IReadOnlyList<HistoryPoint> DailyEnergyHistory => _dailyEnergyHistory;
    public AppTheme Theme => _settings.Theme;
    public string ThemeAction => Theme == AppTheme.Dark ? "Light mode" : "Dark mode";
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
            OnPropertyChanged(nameof(PinButtonText));
            OnPropertyChanged(nameof(PinStatusText));
            _ = SaveSettingsAsync();
        }
    }
    public string PinButtonText => TrayPopupPinned ? "📌  PINNED" : "📍  PIN";
    public string PinStatusText => TrayPopupPinned ? "Pinned · stays visible" : "Unpinned · closes when focus is lost";
    public Visibility SystemOverviewVisibility => Visible(ShowSystemOverview);
    public Visibility PerformanceChartsVisibility => Visible(ShowPerformanceCharts);
    public Visibility StorageVisibility => Visible(ShowStorage);
    public Visibility CoolingVisibility => Visible(ShowCooling);
    public Visibility EnergyVisibility => Visible(ShowEnergy);

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        RaiseLayoutProperties();
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(ThemeAction));
    }

    public async Task ToggleThemeAsync()
    {
        _settings = _settings with { Theme = Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark };
        await SaveSettingsAsync();
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(ThemeAction));
    }

    private void ToggleZeroRpmFans()
    {
        _hideZeroRpmFans = !_hideZeroRpmFans;
        OnPropertyChanged(nameof(HideZeroRpmFans));
        OnPropertyChanged(nameof(ZeroRpmFanAction));
        OnPropertyChanged(nameof(Fans));
        OnPropertyChanged(nameof(FanSummary));
    }

    public async Task ExportEnergyAsync(string path) => await _monitoring.EnergyHistory.ExportAsync(path);

    public async Task<int> ImportEnergyAsync(string path)
    {
        var count = await _monitoring.EnergyHistory.ImportAsync(path);
        await RefreshEnergyAsync();
        return count;
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
        return $"{reading.Value.Value:0} W";
    }

    private string SourceLabel(MetricKind kind)
    {
        var reading = _snapshot[kind];
        return reading.SourceProvider switch
        {
            "HWiNFO Shared Memory" => "HWiNFO BRIDGE · HARDWARE SENSOR",
            "LibreHardwareMonitor" => "FULL ACCESS · HARDWARE SENSOR",
            "Windows Native Telemetry" => "STANDALONE · WINDOWS COUNTER",
            "SysWatt calculated model" => "CALCULATED · MANUALLY ADJUSTABLE",
            _ => "HARDWARE SENSOR"
        };
    }

    private bool _isActive = true;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (Set(ref _isActive, value) && value)
            {
                NotifyAllProperties();
                _ = RefreshEnergyAsync();
            }
        }
    }

    private void NotifyAllProperties() => OnPropertyChanged(string.Empty);

    private IReadOnlyList<HistoryPoint> Window(MetricKind metric)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_settings.GraphWindowMinutes);
        return _monitoring.History.GetWindow(metric, cutoff);
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
        _ => "COOLING"
    };

    private void OnSnapshotUpdated(object? sender, MetricSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (!_isActive) return;

        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!_isActive) return;
            NotifyAllProperties();
            if (DateTimeOffset.UtcNow - _lastEnergyRefresh > TimeSpan.FromSeconds(15)) _ = RefreshEnergyAsync();
        });
    }

    private void OnTelemetryModeChanged(object? sender, SysWatt.Core.Monitoring.TelemetryModeChangedEventArgs change)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _telemetryNoticeDismissal?.Cancel();
            _telemetryNoticeDismissal?.Dispose();
            _telemetryNoticeDismissal = new CancellationTokenSource();
            _telemetryNotice = change.Message;
            OnPropertyChanged(nameof(TelemetrySourceNotice));
            OnPropertyChanged(nameof(TelemetrySourceNoticeVisibility));
            _ = DismissTelemetryNoticeAsync(_telemetryNoticeDismissal.Token);
        });
    }

    private async Task DismissTelemetryNoticeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.AlertBannerSeconds), cancellationToken);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _telemetryNotice = null;
                OnPropertyChanged(nameof(TelemetrySourceNotice));
                OnPropertyChanged(nameof(TelemetrySourceNoticeVisibility));
            });
        }
        catch (OperationCanceledException) { }
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
            _todayHasData = todayRow.HasData;
            _rangeHasData = range.Any(x => x.HasData);
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
                OnPropertyChanged(nameof(EnergyMeasurementNotice));
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
            _alertDismissal?.Cancel();
            _alertDismissal?.Dispose();
            _alertDismissal = new CancellationTokenSource();
            ActiveAlert = alert.Message;
            OnPropertyChanged(nameof(AlertVisibility));
            _ = DismissAlertAsync(_alertDismissal.Token);
        });
    }

    private async Task DismissAlertAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.AlertBannerSeconds), cancellationToken);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ActiveAlert = null;
                OnPropertyChanged(nameof(AlertVisibility));
            });
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _alertDismissal?.Cancel();
        _alertDismissal?.Dispose();
        _telemetryNoticeDismissal?.Cancel();
        _telemetryNoticeDismissal?.Dispose();
        _monitoring.SnapshotUpdated -= OnSnapshotUpdated;
        _monitoring.AlertTriggered -= OnAlertTriggered;
        _monitoring.TelemetryModeChanged -= OnTelemetryModeChanged;
    }
}
