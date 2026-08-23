using System.Text.RegularExpressions;

namespace SysWatt.Core.Sensors;

public sealed partial class SensorNormalizer : ISensorNormalizer
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);

    private sealed record Policy(
        MetricKind Metric,
        SensorKind SensorKind,
        HardwareKind[] Hardware,
        double Minimum,
        double Maximum,
        string[] Prefer,
        string[] Reject);

    private static readonly Policy[] Policies =
    [
        new(MetricKind.CpuUsage, SensorKind.Load, [HardwareKind.Cpu], 0, 100, ["total", "cpu total", "package"], ["core", "max"]),
        new(MetricKind.CpuTemperature, SensorKind.Temperature, [HardwareKind.Cpu], 1, 125, ["package", "tctl", "tdie", "cpu"], ["distance", "limit"]),
        new(MetricKind.CpuPower, SensorKind.Power, [HardwareKind.Cpu], 0.01, 1000, ["package", "cpu package"], ["core", "dram", "soc"]),
        new(MetricKind.GpuUsage, SensorKind.Load, [HardwareKind.GpuNvidia, HardwareKind.GpuAmd, HardwareKind.GpuIntel], 0, 100, ["core", "gpu core", "d3d"], ["memory", "video", "copy"]),
        new(MetricKind.GpuTemperature, SensorKind.Temperature, [HardwareKind.GpuNvidia, HardwareKind.GpuAmd, HardwareKind.GpuIntel], -10, 125, ["core", "gpu core"], ["memory", "hot spot", "junction"]),
        new(MetricKind.GpuPower, SensorKind.Power, [HardwareKind.GpuNvidia, HardwareKind.GpuAmd, HardwareKind.GpuIntel], 0, 1500, ["board", "total", "gpu package", "package"], ["core", "rail"]),
        new(MetricKind.MemoryUsage, SensorKind.Load, [HardwareKind.Memory], 0, 100, ["memory", "used"], []),
        new(MetricKind.StorageActivity, SensorKind.Load, [HardwareKind.Storage], 0, 100, ["total activity", "activity"], ["read", "write"]),
        new(MetricKind.StorageReadRate, SensorKind.Throughput, [HardwareKind.Storage], 0, 1_000_000, ["read rate", "read"], ["write"]),
        new(MetricKind.StorageWriteRate, SensorKind.Throughput, [HardwareKind.Storage], 0, 1_000_000, ["write rate", "write"], ["read"]),
        new(MetricKind.StorageTemperature, SensorKind.Temperature, [HardwareKind.Storage], -10, 125, ["temperature", "composite"], []),
        new(MetricKind.StoragePower, SensorKind.Power, [HardwareKind.Storage], 0.01, 500, ["power", "total"], ["limit"]),
        new(MetricKind.SystemPower, SensorKind.Power, [HardwareKind.Psu, HardwareKind.Ups], 0.01, 100_000,
            ["input power", "wall power", "total power", "output power", "power"], ["limit", "capacity"]),
        new(MetricKind.FanSpeed, SensorKind.Fan, [HardwareKind.Motherboard, HardwareKind.Controller, HardwareKind.Cpu, HardwareKind.GpuNvidia, HardwareKind.GpuAmd], 0, 20000, ["cpu", "system", "fan"], [])
    ];

    public MetricSnapshot Normalize(IReadOnlyList<RawSensorReading> readings, DateTimeOffset now)
    {
        var metrics = new Dictionary<MetricKind, MetricReading>();
        foreach (var policy in Policies)
        {
            var winner = readings
                .Where(r => IsCandidate(r, policy, now))
                .Select(r => (Reading: r, Score: Score(r, policy)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Reading.Descriptor.SensorId, StringComparer.Ordinal)
                .FirstOrDefault();

            metrics[policy.Metric] = winner.Reading is null
                ? MetricReading.Unavailable(policy.Metric, MetricUnits.For(policy.Metric), now, ExplainUnavailable(readings, policy, now))
                : new MetricReading(
                    policy.Metric,
                    winner.Reading.Value,
                    MetricUnits.For(policy.Metric),
                    winner.Reading.Timestamp,
                    false,
                    winner.Reading.Descriptor.SensorId,
                    $"{winner.Reading.Descriptor.HardwareName} / {winner.Reading.Descriptor.SensorName}",
                    $"Selected from {winner.Reading.Descriptor.Provider} with ranking score {winner.Score}.")
                {
                    SourceProvider = winner.Reading.Descriptor.Provider
                };
        }

        var fans = readings
            .Where(reading => IsValidFan(reading, now))
            .GroupBy(
                reading => $"{reading.Descriptor.Provider}|{reading.Descriptor.SensorId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(reading => ProviderPriority(reading.Descriptor.Provider))
                .First())
            .OrderBy(reading => FanHardwareRank(reading.Descriptor.HardwareKind))
            .ThenBy(reading => reading.Descriptor.HardwareName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reading => reading.Descriptor.SensorName, StringComparer.OrdinalIgnoreCase)
            .Select(reading => new FanReading(
                reading.Descriptor.SensorId,
                reading.Descriptor.SensorName,
                reading.Descriptor.HardwareName,
                reading.Descriptor.HardwareKind,
                reading.Value!.Value,
                reading.Timestamp,
                reading.Descriptor.Provider,
                $"{reading.Descriptor.HardwareName} / {reading.Descriptor.SensorName}"))
            .ToArray();

        return new MetricSnapshot(now, metrics) { Fans = fans };
    }

    private static bool IsValidFan(RawSensorReading reading, DateTimeOffset now) =>
        reading.IsAvailable &&
        reading.Descriptor.SensorKind == SensorKind.Fan &&
        reading.Value is { } rpm &&
        double.IsFinite(rpm) &&
        rpm is >= 0 and <= 20_000 &&
        now - reading.Timestamp <= StaleAfter;

    private static int FanHardwareRank(HardwareKind kind) => kind switch
    {
        HardwareKind.Cpu => 0,
        HardwareKind.GpuNvidia or HardwareKind.GpuAmd or HardwareKind.GpuIntel => 1,
        HardwareKind.Motherboard or HardwareKind.Controller => 2,
        _ => 3
    };

    private static bool IsCandidate(RawSensorReading reading, Policy policy, DateTimeOffset now)
    {
        if (!reading.IsAvailable || reading.Value is null || double.IsNaN(reading.Value.Value) || double.IsInfinity(reading.Value.Value)) return false;
        if (now - reading.Timestamp > StaleAfter) return false;
        if (reading.Descriptor.SensorKind != policy.SensorKind || !policy.Hardware.Contains(reading.Descriptor.HardwareKind)) return false;
        return reading.Value >= policy.Minimum && reading.Value <= policy.Maximum;
    }

    private static string ExplainUnavailable(IReadOnlyList<RawSensorReading> readings, Policy policy, DateTimeOffset now)
    {
        var providerFailure = readings.FirstOrDefault(reading =>
            policy.Hardware.Contains(reading.Descriptor.HardwareKind) &&
            !reading.IsAvailable &&
            !string.IsNullOrWhiteSpace(reading.Error));
        if (providerFailure is not null)
            return $"Hardware access failed: {providerFailure.Error}";

        var compatible = readings
            .Where(reading => reading.Descriptor.SensorKind == policy.SensorKind && policy.Hardware.Contains(reading.Descriptor.HardwareKind))
            .ToArray();
        if (compatible.Length == 0)
            return "No compatible hardware sensor was exposed. Check firmware monitoring support and hardware-access permissions.";
        if (compatible.Any(reading => now - reading.Timestamp > StaleAfter))
            return "The compatible hardware sensor stopped updating and is now stale.";
        if (compatible.All(reading => !reading.IsAvailable || reading.Value is null))
            return "The hardware sensor is present but did not return a value.";

        var values = string.Join(", ", compatible
            .Where(reading => reading.Value.HasValue)
            .Take(3)
            .Select(reading => $"{reading.Descriptor.SensorName}={reading.Value:0.##}{reading.Descriptor.Unit}"));
        return $"The hardware sensor returned implausible data ({values}); low-level sensor access may be unavailable.";
    }

    private static int Score(RawSensorReading reading, Policy policy)
    {
        var name = NormalizeName($"{reading.Descriptor.SensorName} {reading.Descriptor.SensorId}");
        var score = policy.Metric == MetricKind.StorageActivity && reading.Descriptor.Provider.Equals("Windows Native Telemetry", StringComparison.OrdinalIgnoreCase) ? 135
            : reading.Descriptor.Provider.Equals("HWiNFO Shared Memory", StringComparison.OrdinalIgnoreCase) ? 120
            : reading.Descriptor.Provider.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) ? 100
            : reading.Descriptor.Provider.Equals("Windows Native Telemetry", StringComparison.OrdinalIgnoreCase) ? 95
            : 50;
        score += policy.Prefer.Select((hint, i) => name.Contains(hint, StringComparison.OrdinalIgnoreCase) ? 40 - (i * 3) : 0).Sum();
        score -= policy.Reject.Count(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase)) * 35;
        return score;
    }

    private static int ProviderPriority(string provider) => provider.Equals("HWiNFO Shared Memory", StringComparison.OrdinalIgnoreCase) ? 3
        : provider.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) ? 2
        : provider.Equals("Windows Native Telemetry", StringComparison.OrdinalIgnoreCase) ? 1
        : 0;

    private static string NormalizeName(string value) => Whitespace().Replace(value.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant(), " ").Trim();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
