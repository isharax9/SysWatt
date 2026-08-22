using SysWatt.Core.Alerts;
using SysWatt.Core.History;
using SysWatt.Core.Sensors;
using SysWatt.Core.Settings;

namespace SysWatt.Core.Monitoring;

public interface IMonitoringService : IAsyncDisposable
{
    MetricSnapshot Current { get; }
    IReadOnlyList<RawSensorReading> LastRawReadings { get; }
    ISessionHistory History { get; }
    event EventHandler<MetricSnapshot>? SnapshotUpdated;
    event EventHandler<AlertEvent>? AlertTriggered;
    void ApplySettings(AppSettings settings);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
