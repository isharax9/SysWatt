using SysWatt.Core.Alerts;
using SysWatt.Core.Power;
using SysWatt.Core.Sensors;

namespace SysWatt.Core.Settings;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 4;
    public MetricKind TrayMetric { get; init; } = MetricKind.EstimatedWallPower;
    public bool StartWithWindows { get; init; }
    public bool StartMinimized { get; init; } = true;
    public bool TrayDashboardPinned { get; init; }
    public int PollingIntervalMilliseconds { get; init; } = 1000;
    public string AccentColor { get; init; } = "#76E6B4";
    public PowerModelSettings Power { get; init; } = new();
    public DashboardLayoutSettings Dashboard { get; init; } = new();
    public List<AlertRule> Alerts { get; init; } = [AlertRule.CreateDefault()];

    public AppSettings Sanitize()
    {
        var interval = Math.Clamp(PollingIntervalMilliseconds, 500, 60_000);
        var power = Power.Validate().Count == 0 ? Power : new PowerModelSettings();
        var alerts = Alerts.Where(a => !string.IsNullOrWhiteSpace(a.Name)
            && double.IsFinite(a.Threshold)
            && a.RequiredDuration >= TimeSpan.Zero && a.RequiredDuration <= TimeSpan.FromDays(1)
            && a.Cooldown >= TimeSpan.Zero && a.Cooldown <= TimeSpan.FromDays(30)).ToList();
        return this with { SchemaVersion = 4, PollingIntervalMilliseconds = interval, Power = power, Alerts = alerts };
    }
}

public sealed record DashboardLayoutSettings(
    bool ShowSystemOverview = true,
    bool ShowPerformanceCharts = true,
    bool ShowStorage = true,
    bool ShowCooling = true,
    bool ShowEnergy = true);

public interface ISettingsStore
{
    string SettingsPath { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
