namespace SysWatt.Core.Sensors;

public enum HardwareKind { Unknown, Cpu, GpuNvidia, GpuAmd, GpuIntel, Memory, Storage, Motherboard, Controller, Network }
public enum SensorKind { Unknown, Load, Temperature, Power, Fan, Data, Throughput, Clock, Voltage, Control }
public enum MetricKind
{
    CpuUsage, CpuTemperature, CpuPower, GpuUsage, GpuTemperature, GpuPower,
    MemoryUsage, StorageActivity, StorageTemperature, FanSpeed,
    EstimatedDcPower, EstimatedWallPower
}

public sealed record SensorDescriptor(
    string Provider,
    string HardwareId,
    string HardwareName,
    HardwareKind HardwareKind,
    string SensorId,
    string SensorName,
    SensorKind SensorKind,
    string Unit);

public sealed record RawSensorReading(
    SensorDescriptor Descriptor,
    double? Value,
    DateTimeOffset Timestamp,
    bool IsAvailable = true,
    string? Error = null);

public sealed record MetricReading(
    MetricKind Metric,
    double? Value,
    string Unit,
    DateTimeOffset Timestamp,
    bool IsStale,
    string? SourceSensorId,
    string? SourceName,
    string? Explanation)
{
    public bool IsAvailable => Value.HasValue && !IsStale;

    public static MetricReading Unavailable(MetricKind metric, string unit, DateTimeOffset timestamp, string reason) =>
        new(metric, null, unit, timestamp, false, null, null, reason);
}

public sealed record FanReading(
    string SensorId,
    string SensorName,
    string HardwareName,
    HardwareKind HardwareKind,
    double Rpm,
    DateTimeOffset Timestamp,
    string Provider,
    string Explanation);

public sealed record MetricSnapshot(DateTimeOffset Timestamp, IReadOnlyDictionary<MetricKind, MetricReading> Metrics)
{
    public IReadOnlyList<FanReading> Fans { get; init; } = [];
    public MetricReading this[MetricKind metric] => Metrics.TryGetValue(metric, out var value)
        ? value
        : MetricReading.Unavailable(metric, MetricUnits.For(metric), Timestamp, "No compatible sensor was detected.");

    public double? Value(MetricKind metric) => this[metric].IsAvailable ? this[metric].Value : null;

    public static MetricSnapshot Empty(DateTimeOffset now) => new(now, new Dictionary<MetricKind, MetricReading>());
}

public static class MetricUnits
{
    public static string For(MetricKind metric) => metric switch
    {
        MetricKind.CpuUsage or MetricKind.GpuUsage or MetricKind.MemoryUsage or MetricKind.StorageActivity => "%",
        MetricKind.CpuTemperature or MetricKind.GpuTemperature or MetricKind.StorageTemperature => "°C",
        MetricKind.CpuPower or MetricKind.GpuPower or MetricKind.EstimatedDcPower or MetricKind.EstimatedWallPower => "W",
        MetricKind.FanSpeed => "RPM",
        _ => string.Empty
    };
}

public interface IRawSensorProvider : IAsyncDisposable
{
    string Name { get; }
    Task<IReadOnlyList<RawSensorReading>> ReadAsync(CancellationToken cancellationToken);
}

public interface ISensorNormalizer
{
    MetricSnapshot Normalize(IReadOnlyList<RawSensorReading> readings, DateTimeOffset now);
}
