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
    public void RejectsZeroCpuTemperatureAndPackagePowerAsUnavailableHardwareData()
    {
        var result = _normalizer.Normalize(
        [
            Reading("temp", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 0),
            Reading("power", "Package", HardwareKind.Cpu, SensorKind.Power, 0)
        ], Now);

        Assert.Null(result.Value(MetricKind.CpuTemperature));
        Assert.Null(result.Value(MetricKind.CpuPower));
        Assert.Contains("implausible", result[MetricKind.CpuTemperature].Explanation);
        Assert.Contains("implausible", result[MetricKind.CpuPower].Explanation);
    }

    [Fact]
    public void ProviderPriorityBreaksOtherwiseEquivalentCandidates()
    {
        var fallback = Reading("fallback", "CPU total", HardwareKind.Cpu, SensorKind.Load, 20, "PerformanceCounter");
        var primary = Reading("primary", "CPU total", HardwareKind.Cpu, SensorKind.Load, 30);
        Assert.Equal(30, _normalizer.Normalize([fallback, primary], Now).Value(MetricKind.CpuUsage));
    }

    [Fact]
    public void EmbeddedHardwareSensorWinsOverWindowsFallbackWhenBothAreValid()
    {
        var direct = Reading("direct", "CPU total", HardwareKind.Cpu, SensorKind.Load, 47);
        var fallback = Reading("fallback", "CPU total", HardwareKind.Cpu, SensorKind.Load, 51, "Windows Native Telemetry");

        var result = _normalizer.Normalize([direct, fallback], Now)[MetricKind.CpuUsage];

        Assert.Equal(47, result.Value);
        Assert.Equal("direct", result.SourceSensorId);
    }

    [Fact]
    public void PreservesEveryFreshValidFanAsANamedReading()
    {
        var readings = new[]
        {
            Reading("/cpu/fan/0", "CPU Fan", HardwareKind.Controller, SensorKind.Fan, 1_225),
            Reading("/gpu/fan/0", "GPU Fan", HardwareKind.GpuNvidia, SensorKind.Fan, 1_640),
            Reading("/board/fan/2", "System Fan #2", HardwareKind.Motherboard, SensorKind.Fan, 880),
            Reading("/board/fan/stale", "Stale Fan", HardwareKind.Motherboard, SensorKind.Fan, 900) with { Timestamp = Now.AddSeconds(-10) },
            Reading("/board/fan/invalid", "Invalid Fan", HardwareKind.Motherboard, SensorKind.Fan, 25_000)
        };

        var fans = _normalizer.Normalize(readings, Now).Fans;

        Assert.Equal(3, fans.Count);
        Assert.Contains(fans, fan => fan.SensorId == "/cpu/fan/0" && fan.Rpm == 1_225);
        Assert.Contains(fans, fan => fan.SensorId == "/gpu/fan/0" && fan.Rpm == 1_640);
        Assert.Contains(fans, fan => fan.SensorId == "/board/fan/2" && fan.Rpm == 880);
    }

    private static RawSensorReading Reading(string id, string name, HardwareKind hardware, SensorKind sensor, double? value, string provider = "LibreHardwareMonitor") =>
        new(new SensorDescriptor(provider, "/hardware/0", "Fixture hardware", hardware, id, name, sensor, ""), value, Now);
}
