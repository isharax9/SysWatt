using SysWatt.Core.Sensors;

namespace SysWatt.Core.Tests;

public sealed class SensorNormalizerTests
{
    private readonly SensorNormalizer _normalizer = new();
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RanksPackagePowerAboveCorePowerWithoutExactNameDependency()
    {
        var readings = new[]
        {
            Reading("/cpu/0/power/1", "Core cluster energy", HardwareKind.Cpu, SensorKind.Power, 22),
            Reading("/cpu/0/power/0", "Socket Package Consumption", HardwareKind.Cpu, SensorKind.Power, 64)
        };
        var result = _normalizer.Normalize(readings, Now)[MetricKind.CpuPower];
        Assert.Equal(64, result.Value);
        Assert.Equal("/cpu/0/power/0", result.SourceSensorId);
    }

    [Fact]
    public void InvalidAndStaleValuesAreUnavailableRatherThanZero()
    {
        var stale = Reading("temp", "CPU package", HardwareKind.Cpu, SensorKind.Temperature, 50) with { Timestamp = Now.AddSeconds(-10) };
        var invalid = Reading("load", "CPU total", HardwareKind.Cpu, SensorKind.Load, 150);
        var result = _normalizer.Normalize([stale, invalid], Now);
        Assert.Null(result[MetricKind.CpuTemperature].Value);
        Assert.Null(result[MetricKind.CpuUsage].Value);
    }

    [Fact]
    public void ProviderPriorityBreaksOtherwiseEquivalentCandidates()
    {
        var fallback = Reading("fallback", "CPU total", HardwareKind.Cpu, SensorKind.Load, 20, "PerformanceCounter");
        var primary = Reading("primary", "CPU total", HardwareKind.Cpu, SensorKind.Load, 30);
        Assert.Equal(30, _normalizer.Normalize([fallback, primary], Now).Value(MetricKind.CpuUsage));
    }

    private static RawSensorReading Reading(string id, string name, HardwareKind hardware, SensorKind sensor, double? value, string provider = "LibreHardwareMonitor") =>
        new(new SensorDescriptor(provider, "/hardware/0", "Fixture hardware", hardware, id, name, sensor, ""), value, Now);
}
