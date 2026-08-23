using SysWatt.Core.Alerts;
using SysWatt.Core.History;
using SysWatt.Core.Energy;
using SysWatt.Core.Sensors;
using SysWatt.Core.Settings;

namespace SysWatt.Core.Monitoring;

public sealed class TelemetryModeChangedEventArgs : EventArgs
{
    public TelemetryModeChangedEventArgs(TelemetrySource previous, TelemetrySource current, string message)
    {
        Previous = previous;
        Current = current;
        Message = message;
    }

    public TelemetrySource Previous { get; }
    public TelemetrySource Current { get; }
    public string Message { get; }
}

public interface IMonitoringService : IAsyncDisposable
{
    MetricSnapshot Current { get; }
    IReadOnlyList<RawSensorReading> LastRawReadings { get; }
    ISessionHistory History { get; }
    IEnergyHistoryStore EnergyHistory { get; }
    event EventHandler<MetricSnapshot>? SnapshotUpdated;
    event EventHandler<AlertEvent>? AlertTriggered;
    event EventHandler<TelemetryModeChangedEventArgs>? TelemetryModeChanged;
    void ApplySettings(AppSettings settings);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
