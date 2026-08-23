using Microsoft.Extensions.Logging;
using SysWatt.Core.Alerts;
using SysWatt.Core.History;
using SysWatt.Core.Energy;
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
    private readonly IHardwareInventoryService _inventory;
    private readonly IAlertEvaluator _alerts;
    private readonly ILogger<MonitoringService> _logger;
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AppSettings _settings;
    private TelemetrySource _lastSource = TelemetrySource.Standalone;
    private bool _hasSource;

    public MetricSnapshot Current { get; private set; } = MetricSnapshot.Empty(DateTimeOffset.UtcNow);
    public IReadOnlyList<RawSensorReading> LastRawReadings { get; private set; } = [];
    public ISessionHistory History { get; }
    public IEnergyHistoryStore EnergyHistory { get; }
    public event EventHandler<MetricSnapshot>? SnapshotUpdated;
    public event EventHandler<AlertEvent>? AlertTriggered;
    public event EventHandler<TelemetryModeChangedEventArgs>? TelemetryModeChanged;

    public MonitoringService(IEnumerable<IRawSensorProvider> providers, ISensorNormalizer normalizer,
        IPowerEstimationService power, IHardwareInventoryService inventory,
        IAlertEvaluator alerts, ISessionHistory history, IEnergyHistoryStore energyHistory,
        AppSettings settings, ILogger<MonitoringService> logger)
    {
        _providers = providers.ToArray();
        _normalizer = normalizer;
        _power = power;
        _inventory = inventory;
        _alerts = alerts;
        History = history;
        EnergyHistory = energyHistory;
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
        var inventory = await _inventory.GetAsync(cancellationToken).ConfigureAwait(false);
        var detectedCoolingHeaders = normalized.Fans.Count(fan => fan.Rpm >= 100 &&
            fan.HardwareKind is not (HardwareKind.GpuAmd or HardwareKind.GpuIntel or HardwareKind.GpuNvidia));
        var effectivePower = settings.Power.ApplyInventory(inventory, detectedCoolingHeaders);
        var estimate = _power.Calculate(
            normalized.Value(MetricKind.CpuPower), normalized.Value(MetricKind.GpuPower), effectivePower,
            normalized.Value(MetricKind.CpuUsage), normalized.Value(MetricKind.GpuUsage), normalized.Value(MetricKind.StorageActivity),
            normalized.Value(MetricKind.StorageReadRate), normalized.Value(MetricKind.StorageWriteRate), normalized.Value(MetricKind.StoragePower));
        var metrics = normalized.Metrics.ToDictionary(x => x.Key, x => x.Value);
        if (estimate.CpuIsModeled)
            metrics[MetricKind.CpuPower] = ModeledPower(MetricKind.CpuPower, estimate.EffectiveCpuWatts, normalized.Value(MetricKind.CpuUsage), now);
        if (estimate.GpuIsModeled)
            metrics[MetricKind.GpuPower] = ModeledPower(MetricKind.GpuPower, estimate.EffectiveGpuWatts, normalized.Value(MetricKind.GpuUsage), now);
        if (estimate.StorageIsModeled)
            metrics[MetricKind.StoragePower] = Calculated(MetricKind.StoragePower, estimate.EffectiveStorageWatts, now,
                $"Activity-aware model for {inventory.StorageSummary}");
        metrics[MetricKind.BaseSystemPower] = Calculated(MetricKind.BaseSystemPower, estimate.BaseSystemWatts, now,
            $"Configured motherboard and memory allowance · {inventory.Motherboard}");
        metrics[MetricKind.CoolingPower] = Calculated(MetricKind.CoolingPower, estimate.CoolingWatts, now,
            $"{effectivePower.FanCount} configured/detected CPU and case fans plus pumps and controllers. GPU fans remain inside GPU board power.");
        metrics[MetricKind.ExternalPower] = Calculated(MetricKind.ExternalPower, estimate.PeripheralDcWatts + estimate.ExternalAcWatts, now,
            $"{effectivePower.DisplayCount} display(s), USB devices, removable/external peripherals, and other wall loads.");
        metrics[MetricKind.EstimatedDcPower] = Calculated(MetricKind.EstimatedDcPower, estimate.EstimatedDcWatts, now, estimate.Confidence);
        metrics[MetricKind.EstimatedWallPower] = Calculated(MetricKind.EstimatedWallPower, estimate.EstimatedWallWatts, now,
            $"{estimate.Confidence} {estimate.Formula}");
        var source = DetermineSource(all);
        var sourceDiagnostic = DetermineSourceDiagnostic(all, source);
        Current = new MetricSnapshot(now, metrics)
        {
            Fans = normalized.Fans,
            Source = source,
            SourceDiagnostic = sourceDiagnostic
        };
        History.Add(Current);
        await EnergyHistory.RecordSampleAsync(now, estimate.EstimatedWallWatts, TelemetrySource.HybridModel, cancellationToken).ConfigureAwait(false);
        if (_hasSource && source != _lastSource)
        {
            var message = source == TelemetrySource.HWiNFOBridge
                ? "HWiNFO Shared Memory connected. SysWatt is using HWiNFO-reported hardware telemetry."
                : source == TelemetrySource.FullHardwareAccess
                    ? "HWiNFO Shared Memory unavailable. SysWatt fell back to Full Hardware Access."
                    : "Low-level hardware telemetry unavailable. Exact sensors show N/A; the configured hybrid model remains available.";
            TelemetryModeChanged?.Invoke(this, new TelemetryModeChangedEventArgs(_lastSource, source, message));
        }
        _lastSource = source;
        _hasSource = true;
        SnapshotUpdated?.Invoke(this, Current);
        foreach (var alert in _alerts.Evaluate(settings.Alerts, Current)) AlertTriggered?.Invoke(this, alert);
    }

    private static MetricReading ModeledPower(MetricKind metric, double watts, double? usage, DateTimeOffset now) =>
        new(metric, watts, "W", now, false, null, "SysWatt utilization model",
            usage.HasValue
                ? $"Calculated from {usage:0}% live utilization and the manually adjustable idle/peak envelope."
                : "Calculated from the configured idle value because no live utilization counter was available.")
        { SourceProvider = "SysWatt calculated model" };

    private static MetricReading Calculated(MetricKind metric, double watts, DateTimeOffset now, string explanation) =>
        new(metric, watts, "W", now, false, null, "SysWatt hardware-informed model", explanation)
        { SourceProvider = "SysWatt calculated model" };

    private static TelemetrySource DetermineSource(IReadOnlyList<RawSensorReading> readings)
    {
        if (readings.Any(reading => reading.IsAvailable && reading.Descriptor.Provider.Equals("HWiNFO Shared Memory", StringComparison.OrdinalIgnoreCase)))
            return TelemetrySource.HWiNFOBridge;
        if (readings.Any(reading => reading.IsAvailable && reading.Descriptor.Provider.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase)))
            return TelemetrySource.FullHardwareAccess;
        return TelemetrySource.Standalone;
    }

    private static string DetermineSourceDiagnostic(IReadOnlyList<RawSensorReading> readings, TelemetrySource source)
    {
        if (source == TelemetrySource.HWiNFOBridge)
            return "HWiNFO Shared Memory · hardware-reported telemetry";
        if (source == TelemetrySource.FullHardwareAccess)
        {
            var bridgeStatus = readings.FirstOrDefault(reading =>
                reading.Descriptor.Provider.Equals("HWiNFO Shared Memory", StringComparison.OrdinalIgnoreCase) && !reading.IsAvailable)?.Error;
            return bridgeStatus is { Length: > 0 }
                ? $"SysWatt Full Hardware Access · LibreHardwareMonitor/PawnIO · HWiNFO bridge unavailable"
                : "SysWatt Full Hardware Access · LibreHardwareMonitor/PawnIO";
        }
        var bridge = readings.FirstOrDefault(reading =>
            reading.Descriptor.Provider.Equals("HWiNFO Shared Memory", StringComparison.OrdinalIgnoreCase) && !reading.IsAvailable);
        return bridge?.Error is { Length: > 0 }
            ? $"Standalone Mode · {bridge.Error}"
            : "Standalone Mode · Windows/vendor counters · unavailable hardware sensors show N/A";
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
        await EnergyHistory.DisposeAsync().ConfigureAwait(false);
    }
}
