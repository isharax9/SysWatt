using Microsoft.Extensions.Logging;
using SysWatt.Core.Alerts;
using SysWatt.Core.History;
using SysWatt.Core.Monitoring;
using SysWatt.Core.Power;
using SysWatt.Core.Sensors;
using SysWatt.Core.Settings;

namespace SysWatt.Infrastructure.Monitoring;

public sealed class MonitoringService : IMonitoringService
{
    private readonly IReadOnlyList<IRawSensorProvider> _providers;
    private readonly ISensorNormalizer _normalizer;
    private readonly IPowerEstimationService _power;
    private readonly IAlertEvaluator _alerts;
    private readonly ILogger<MonitoringService> _logger;
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AppSettings _settings;

    public MetricSnapshot Current { get; private set; } = MetricSnapshot.Empty(DateTimeOffset.UtcNow);
    public IReadOnlyList<RawSensorReading> LastRawReadings { get; private set; } = [];
    public ISessionHistory History { get; }
    public event EventHandler<MetricSnapshot>? SnapshotUpdated;
    public event EventHandler<AlertEvent>? AlertTriggered;

    public MonitoringService(IEnumerable<IRawSensorProvider> providers, ISensorNormalizer normalizer,
        IPowerEstimationService power, IAlertEvaluator alerts, ISessionHistory history,
        AppSettings settings, ILogger<MonitoringService> logger)
    {
        _providers = providers.ToArray();
        _normalizer = normalizer;
        _power = power;
        _alerts = alerts;
        History = history;
        _settings = settings;
        _logger = logger;
    }

    public void ApplySettings(AppSettings settings) => Volatile.Write(ref _settings, settings.Sanitize());

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_loop is not null) return Task.CompletedTask;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await PollOnceAsync(cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested)
        {
            var settings = Volatile.Read(ref _settings);
            try
            {
                await Task.Delay(settings.PollingIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
                await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Monitoring cycle failed; the next cycle will still run."); }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var all = new List<RawSensorReading>();
        foreach (var provider in _providers)
        {
            if (provider.Name.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) &&
                all.Any(reading => reading.Descriptor.Provider.Equals("HWiNFO Shared Memory", StringComparison.OrdinalIgnoreCase) && reading.IsAvailable))
            {
                _logger.LogDebug("HWiNFO is publishing sensors; skipping concurrent LibreHardwareMonitor hardware access for this cycle.");
                continue;
            }
            try
            {
                all.AddRange(await provider.ReadAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sensor provider {Provider} failed; other providers remain active.", provider.Name);
                var descriptor = new SensorDescriptor(
                    provider.Name,
                    $"/{provider.Name}/provider-error",
                    provider.Name,
                    HardwareKind.Unknown,
                    $"/{provider.Name}/provider-error",
                    "Provider read failure",
                    SensorKind.Unknown,
                    string.Empty);
                all.Add(new RawSensorReading(descriptor, null, DateTimeOffset.UtcNow, false, ex.Message));
            }
        }

        var now = DateTimeOffset.UtcNow;
        LastRawReadings = all.ToArray();
        var normalized = _normalizer.Normalize(all, now);
        var settings = Volatile.Read(ref _settings);
        var estimate = _power.Calculate(normalized.Value(MetricKind.CpuPower), normalized.Value(MetricKind.GpuPower), settings.Power);
        var metrics = normalized.Metrics.ToDictionary(x => x.Key, x => x.Value);
        metrics[MetricKind.EstimatedDcPower] = new(MetricKind.EstimatedDcPower, estimate.EstimatedDcWatts, "W", now, false, null, "SysWatt power model", estimate.Confidence);
        metrics[MetricKind.EstimatedWallPower] = new(MetricKind.EstimatedWallPower, estimate.EstimatedWallWatts, "W", now, false, null, "SysWatt power model", $"{estimate.Confidence} {estimate.Formula}");
        Current = new MetricSnapshot(now, metrics) { Fans = normalized.Fans };
        History.Add(Current);
        SnapshotUpdated?.Invoke(this, Current);
        foreach (var alert in _alerts.Evaluate(settings.Alerts, Current)) AlertTriggered?.Invoke(this, alert);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? loop;
        lock (_lifecycleGate)
        {
            _cts?.Cancel();
            loop = _loop;
            _loop = null;
        }
        if (loop is not null) await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        foreach (var provider in _providers) await provider.DisposeAsync().ConfigureAwait(false);
    }
}
