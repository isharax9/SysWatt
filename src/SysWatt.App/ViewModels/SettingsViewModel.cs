using System.Collections.ObjectModel;
using SysWatt.App.Commands;
using SysWatt.Core.Alerts;
using SysWatt.Core.Monitoring;
using SysWatt.Core.Power;
using SysWatt.Core.Sensors;
using SysWatt.Core.Settings;
using SysWatt.Infrastructure.Windows;

namespace SysWatt.App.ViewModels;

public sealed class AlertRuleViewModel : ViewModelBase
{
    private string _name;
    private MetricKind _metric;
    private ComparisonOperator _operator;
    private double _threshold;
    private double _durationSeconds;
    private double _cooldownSeconds;
    private AlertSeverity _severity;
    private bool _enabled;
    private bool _desktop;
    private bool _inApp;

    public Guid Id { get; }
    public Array MetricChoices { get; } = Enum.GetValues<MetricKind>();
    public Array OperatorChoices { get; } = Enum.GetValues<ComparisonOperator>();
    public Array SeverityChoices { get; } = Enum.GetValues<AlertSeverity>();
    public string Name { get => _name; set => Set(ref _name, value); }
    public MetricKind Metric { get => _metric; set => Set(ref _metric, value); }
    public ComparisonOperator Operator { get => _operator; set => Set(ref _operator, value); }
    public double Threshold { get => _threshold; set => Set(ref _threshold, value); }
    public double DurationSeconds { get => _durationSeconds; set => Set(ref _durationSeconds, value); }
    public double CooldownSeconds { get => _cooldownSeconds; set => Set(ref _cooldownSeconds, value); }
    public AlertSeverity Severity { get => _severity; set => Set(ref _severity, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool DesktopNotification { get => _desktop; set => Set(ref _desktop, value); }
    public bool InApp { get => _inApp; set => Set(ref _inApp, value); }

    public AlertRuleViewModel(AlertRule rule)
    {
        Id = rule.Id; _name = rule.Name; _metric = rule.Metric; _operator = rule.Operator;
        _threshold = rule.Threshold; _durationSeconds = rule.RequiredDuration.TotalSeconds;
        _cooldownSeconds = rule.Cooldown.TotalSeconds; _severity = rule.Severity; _enabled = rule.Enabled;
        _desktop = rule.ShowDesktopNotification; _inApp = rule.ShowInApp;
    }

    public AlertRule ToModel() => new(Id, Name.Trim(), Metric, Operator, Threshold,
        TimeSpan.FromSeconds(DurationSeconds), TimeSpan.FromSeconds(CooldownSeconds), Severity, Enabled, DesktopNotification, InApp);
}

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;
    private readonly IStartupRegistrationService _startup;
    private readonly IMonitoringService _monitoring;
    private MetricKind _trayMetric;
    private AppTheme _theme;
    private bool _startWithWindows;
    private bool _startMinimized;
    private int _graphWindowMinutes;
    private int _alertBannerSeconds;
    private int _pollingIntervalMilliseconds;
    private double _baseWatts;
    private double _storageWatts;
    private int _fanCount;
    private int _cpuCoolerFanCount;
    private int _caseFanCount;
    private double _wattsPerFan;
    private double _otherCoolingWatts;
    private double _usbPeripheralWatts;
    private double _displayWatts;
    private double _externalPeripheralWatts;
    private double _otherWallWatts;
    private double _efficiencyPercent;
    private double _cpuIdleWatts;
    private double _cpuPeakWatts;
    private double _gpuIdleWatts;
    private double _gpuPeakWatts;
    private int _storageDeviceCount;
    private double _storageIdleWattsPerDevice;
    private double _storageThroughputCeilingMBps;
    private bool _autoDetectStorage;
    private bool _autoDetectCooling;
    private bool _autoDetectDisplays;
    private int _displayCount;
    private bool _autoDetectRemovablePeripherals;
    private double _wattsPerDetectedPeripheral;
    private readonly DashboardLayoutSettings _dashboardLayout;
    private readonly bool _trayDashboardPinned;
    private string _accentColor;
    private AlertRuleViewModel? _selectedAlert;
    private string? _error;

    public event EventHandler<AppSettings>? Saved;
    public event EventHandler? RequestClose;
    public Array TrayMetricChoices { get; } = new[] { MetricKind.EstimatedWallPower, MetricKind.EstimatedDcPower, MetricKind.CpuPower, MetricKind.GpuPower, MetricKind.CpuTemperature, MetricKind.GpuTemperature, MetricKind.CpuUsage, MetricKind.GpuUsage };
    public Array ThemeChoices { get; } = Enum.GetValues<AppTheme>();
    public ObservableCollection<AlertRuleViewModel> Alerts { get; }
    public MetricKind TrayMetric { get => _trayMetric; set => Set(ref _trayMetric, value); }
    public AppTheme Theme { get => _theme; set => Set(ref _theme, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }
    public int GraphWindowMinutes { get => _graphWindowMinutes; set => Set(ref _graphWindowMinutes, value); }
    public int AlertBannerSeconds { get => _alertBannerSeconds; set => Set(ref _alertBannerSeconds, value); }
    public double BaseWatts { get => _baseWatts; set => Set(ref _baseWatts, value); }
    public double StorageWatts { get => _storageWatts; set => Set(ref _storageWatts, value); }
    public int FanCount { get => _fanCount; set { if (Set(ref _fanCount, value)) OnPropertyChanged(nameof(CoolingRatedPower)); } }
    public int CpuCoolerFanCount { get => _cpuCoolerFanCount; set { if (Set(ref _cpuCoolerFanCount, value)) UpdateTotalFanCount(); } }
    public int CaseFanCount { get => _caseFanCount; set { if (Set(ref _caseFanCount, value)) UpdateTotalFanCount(); } }
    public double WattsPerFan { get => _wattsPerFan; set { if (Set(ref _wattsPerFan, value)) OnPropertyChanged(nameof(CoolingRatedPower)); } }
    public double OtherCoolingWatts { get => _otherCoolingWatts; set { if (Set(ref _otherCoolingWatts, value)) OnPropertyChanged(nameof(CoolingRatedPower)); } }
    public string CoolingRatedPower => $"{Math.Max(0, FanCount * WattsPerFan + OtherCoolingWatts):0.0} W rated maximum";
    public double UsbPeripheralWatts { get => _usbPeripheralWatts; set => Set(ref _usbPeripheralWatts, value); }
    public double DisplayWatts { get => _displayWatts; set => Set(ref _displayWatts, value); }
    public double ExternalPeripheralWatts { get => _externalPeripheralWatts; set => Set(ref _externalPeripheralWatts, value); }
    public double OtherWallWatts { get => _otherWallWatts; set => Set(ref _otherWallWatts, value); }
    public double EfficiencyPercent { get => _efficiencyPercent; set => Set(ref _efficiencyPercent, value); }
    public double CpuIdleWatts { get => _cpuIdleWatts; set => Set(ref _cpuIdleWatts, value); }
    public double CpuPeakWatts { get => _cpuPeakWatts; set => Set(ref _cpuPeakWatts, value); }
    public double GpuIdleWatts { get => _gpuIdleWatts; set => Set(ref _gpuIdleWatts, value); }
    public double GpuPeakWatts { get => _gpuPeakWatts; set => Set(ref _gpuPeakWatts, value); }
    public int StorageDeviceCount { get => _storageDeviceCount; set => Set(ref _storageDeviceCount, value); }
    public double StorageIdleWattsPerDevice { get => _storageIdleWattsPerDevice; set => Set(ref _storageIdleWattsPerDevice, value); }
    public double StorageThroughputCeilingMBps { get => _storageThroughputCeilingMBps; set => Set(ref _storageThroughputCeilingMBps, value); }
    public bool AutoDetectStorage { get => _autoDetectStorage; set => Set(ref _autoDetectStorage, value); }
    public bool AutoDetectCooling { get => _autoDetectCooling; set => Set(ref _autoDetectCooling, value); }
    public bool AutoDetectDisplays { get => _autoDetectDisplays; set => Set(ref _autoDetectDisplays, value); }
    public int DisplayCount { get => _displayCount; set => Set(ref _displayCount, value); }
    public bool AutoDetectRemovablePeripherals { get => _autoDetectRemovablePeripherals; set => Set(ref _autoDetectRemovablePeripherals, value); }
    public double WattsPerDetectedPeripheral { get => _wattsPerDetectedPeripheral; set => Set(ref _wattsPerDetectedPeripheral, value); }
    public string AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }
    public AlertRuleViewModel? SelectedAlert { get => _selectedAlert; set => Set(ref _selectedAlert, value); }
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public RelayCommand AddAlertCommand { get; }
    public RelayCommand DuplicateAlertCommand { get; }
    public RelayCommand DeleteAlertCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public SettingsViewModel(AppSettings settings, ISettingsStore store, IStartupRegistrationService startup, IMonitoringService monitoring)
    {
        _store = store; _startup = startup; _monitoring = monitoring;
        _trayMetric = settings.TrayMetric; _theme = settings.Theme; _startWithWindows = settings.StartWithWindows;
        _startMinimized = settings.StartMinimized; _baseWatts = settings.Power.BaseSystemWatts;
        _graphWindowMinutes = settings.GraphWindowMinutes; _alertBannerSeconds = settings.AlertBannerSeconds;
        _pollingIntervalMilliseconds = settings.PollingIntervalMilliseconds;
        _storageWatts = settings.Power.StorageWatts; _fanCount = settings.Power.FanCount;
        _cpuCoolerFanCount = settings.Power.FanCount > 0 ? 1 : 0;
        _caseFanCount = Math.Max(0, settings.Power.FanCount - _cpuCoolerFanCount);
        _wattsPerFan = settings.Power.WattsPerFan; _otherCoolingWatts = settings.Power.OtherCoolingWatts;
        _usbPeripheralWatts = settings.Power.UsbPeripheralWatts; _displayWatts = settings.Power.DisplayWatts;
        _externalPeripheralWatts = settings.Power.ExternalPeripheralWatts; _otherWallWatts = settings.Power.OtherWallWatts;
        _efficiencyPercent = settings.Power.PsuEfficiency * 100; _accentColor = settings.AccentColor;
        _cpuIdleWatts = settings.Power.CpuIdleWatts; _cpuPeakWatts = settings.Power.CpuPeakWatts;
        _gpuIdleWatts = settings.Power.GpuIdleWatts; _gpuPeakWatts = settings.Power.GpuPeakWatts;
        _storageDeviceCount = settings.Power.StorageDeviceCount; _storageIdleWattsPerDevice = settings.Power.StorageIdleWattsPerDevice;
        _storageThroughputCeilingMBps = settings.Power.StorageThroughputCeilingMBps;
        _autoDetectStorage = settings.Power.AutoDetectStorage; _autoDetectCooling = settings.Power.AutoDetectCooling;
        _autoDetectDisplays = settings.Power.AutoDetectDisplays; _displayCount = settings.Power.DisplayCount;
        _autoDetectRemovablePeripherals = settings.Power.AutoDetectRemovablePeripherals;
        _wattsPerDetectedPeripheral = settings.Power.WattsPerDetectedPeripheral;
        _dashboardLayout = settings.Dashboard; _trayDashboardPinned = settings.TrayDashboardPinned;
        Alerts = new(settings.Alerts.Select(a => new AlertRuleViewModel(a)));
        AddAlertCommand = new(() => { var row = new AlertRuleViewModel(AlertRule.CreateDefault() with { Name = "New alert" }); Alerts.Add(row); SelectedAlert = row; });
        DuplicateAlertCommand = new(() =>
        {
            if (SelectedAlert is null) return;
            var copy = new AlertRuleViewModel(SelectedAlert.ToModel() with { Id = Guid.NewGuid(), Name = SelectedAlert.Name + " copy" });
            Alerts.Add(copy); SelectedAlert = copy;
        });
        DeleteAlertCommand = new(() => { if (SelectedAlert is not null) Alerts.Remove(SelectedAlert); });
        SaveCommand = new(SaveAsync);
        CancelCommand = new(() => RequestClose?.Invoke(this, EventArgs.Empty));
    }

    private async Task SaveAsync()
    {
        Error = null;
        var power = new PowerModelSettings(
            BaseWatts,
            EfficiencyPercent / 100d,
            StorageWatts,
            FanCount,
            WattsPerFan,
            OtherCoolingWatts,
            UsbPeripheralWatts,
            DisplayWatts,
            ExternalPeripheralWatts,
            OtherWallWatts,
            CpuIdleWatts,
            CpuPeakWatts,
            GpuIdleWatts,
            GpuPeakWatts,
            StorageDeviceCount,
            StorageIdleWattsPerDevice,
            StorageThroughputCeilingMBps,
            AutoDetectStorage,
            AutoDetectCooling,
            AutoDetectDisplays,
            DisplayCount,
            AutoDetectRemovablePeripherals,
            WattsPerDetectedPeripheral);
        var validation = power.Validate().ToList();
        if (Alerts.Any(a => string.IsNullOrWhiteSpace(a.Name))) validation.Add("Every alert needs a name.");
        if (Alerts.Any(a => a.DurationSeconds < 0 || a.CooldownSeconds < 0)) validation.Add("Alert duration and cooldown cannot be negative.");
        if (validation.Count > 0) { Error = string.Join(" ", validation); return; }
        try
        {
            var settings = new AppSettings
            {
                TrayMetric = TrayMetric, StartWithWindows = StartWithWindows, StartMinimized = StartMinimized,
                Theme = Theme,
                GraphWindowMinutes = GraphWindowMinutes, AlertBannerSeconds = AlertBannerSeconds,
                PollingIntervalMilliseconds = _pollingIntervalMilliseconds,
                TrayDashboardPinned = _trayDashboardPinned, Dashboard = _dashboardLayout,
                Power = power, AccentColor = AccentColor, Alerts = Alerts.Select(a => a.ToModel()).ToList()
            }.Sanitize();
            _startup.SetEnabled(settings.StartWithWindows);
            await _store.SaveAsync(settings);
            _monitoring.ApplySettings(settings);
            Saved?.Invoke(this, settings);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Error = $"Settings could not be saved: {ex.Message}"; }
    }

    private void UpdateTotalFanCount()
    {
        FanCount = Math.Max(0, CpuCoolerFanCount) + Math.Max(0, CaseFanCount);
        OnPropertyChanged(nameof(CoolingRatedPower));
    }
}
