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
        new(MetricKind.CpuTemperature, SensorKind.Temperature, [HardwareKind.Cpu], -10, 125, ["package", "tctl", "tdie", "cpu"], ["distance", "limit"]),
        new(MetricKind.CpuPower, SensorKind.Power, [HardwareKind.Cpu], 0, 1000, ["package", "cpu package"], ["core", "dram", "soc"]),
        new(MetricKind.GpuUsage, SensorKind.Load, [HardwareKind.GpuNvidia, HardwareKind.GpuAmd, HardwareKind.GpuIntel], 0, 100, ["core", "gpu core", "d3d"], ["memory", "video", "copy"]),
        new(MetricKind.GpuTemperature, SensorKind.Temperature, [HardwareKind.GpuNvidia, HardwareKind.GpuAmd, HardwareKind.GpuIntel], -10, 125, ["core", "gpu core"], ["memory", "hot spot", "junction"]),
        new(MetricKind.GpuPower, SensorKind.Power, [HardwareKind.GpuNvidia, HardwareKind.GpuAmd, HardwareKind.GpuIntel], 0, 1500, ["board", "total", "gpu package", "package"], ["core", "rail"]),
        new(MetricKind.MemoryUsage, SensorKind.Load, [HardwareKind.Memory], 0, 100, ["memory", "used"], []),
        new(MetricKind.StorageActivity, SensorKind.Load, [HardwareKind.Storage], 0, 100, ["total activity", "activity"], ["read", "write"]),
        new(MetricKind.StorageTemperature, SensorKind.Temperature, [HardwareKind.Storage], -10, 125, ["temperature", "composite"], []),
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
                ? MetricReading.Unavailable(policy.Metric, MetricUnits.For(policy.Metric), now, "No valid compatible sensor was detected.")
                : new MetricReading(
                    policy.Metric,
                    winner.Reading.Value,
                    MetricUnits.For(policy.Metric),
                    winner.Reading.Timestamp,
                    false,
                    winner.Reading.Descriptor.SensorId,
                    $"{winner.Reading.Descriptor.HardwareName} / {winner.Reading.Descriptor.SensorName}",
                    $"Selected from {winner.Reading.Descriptor.Provider} with ranking score {winner.Score}.");
        }

        return new MetricSnapshot(now, metrics);
    }

    private static bool IsCandidate(RawSensorReading reading, Policy policy, DateTimeOffset now)
    {
        if (!reading.IsAvailable || reading.Value is null || double.IsNaN(reading.Value.Value) || double.IsInfinity(reading.Value.Value)) return false;
        if (now - reading.Timestamp > StaleAfter) return false;
        if (reading.Descriptor.SensorKind != policy.SensorKind || !policy.Hardware.Contains(reading.Descriptor.HardwareKind)) return false;
        return reading.Value >= policy.Minimum && reading.Value <= policy.Maximum;
    }

    private static int Score(RawSensorReading reading, Policy policy)
    {
        var name = NormalizeName($"{reading.Descriptor.SensorName} {reading.Descriptor.SensorId}");
        var score = reading.Descriptor.Provider.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) ? 100 : 50;
        score += policy.Prefer.Select((hint, i) => name.Contains(hint, StringComparison.OrdinalIgnoreCase) ? 40 - (i * 3) : 0).Sum();
        score -= policy.Reject.Count(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase)) * 35;
        return score;
    }

    private static string NormalizeName(string value) => Whitespace().Replace(value.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant(), " ").Trim();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
