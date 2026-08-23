using System.Diagnostics;
using Hwinfo.SharedMemory;
using Microsoft.Extensions.Logging;
using SysWatt.Core.Sensors;

namespace SysWatt.Infrastructure.Hardware;

/// <summary>
/// Reads the sensor snapshot published by a running HWiNFO instance. When this
/// provider has data, MonitoringService avoids opening a second low-level
/// monitor against the same SMU/Super-I/O hardware.
/// </summary>
public sealed class HwinfoSharedMemoryProvider : IRawSensorProvider
{
    private readonly SharedMemoryReader _reader = new(100);
    private readonly ILogger<HwinfoSharedMemoryProvider> _logger;

    public string Name => "HWiNFO Shared Memory";

    public HwinfoSharedMemoryProvider(ILogger<HwinfoSharedMemoryProvider> logger) => _logger = logger;

    public Task<IReadOnlyList<RawSensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var result = _reader.ReadLocal()
                .Select(reading => Map(reading, now))
                .Where(reading => reading is not null)
                .Cast<RawSensorReading>()
                .ToArray();
            if (result.Length > 0)
                _logger.LogDebug("Read {Count} sensors from HWiNFO shared memory.", result.Length);
            return Task.FromResult<IReadOnlyList<RawSensorReading>>(result);
        }
        catch (FileNotFoundException)
        {
            // HWiNFO is not running or Shared Memory Support is disabled. This
            // is an expected state; LibreHardwareMonitor remains the fallback.
            if (IsHwinfoRunning())
            {
                var now = DateTimeOffset.UtcNow;
                var descriptor = new SensorDescriptor(
                    Name,
                    "/hwinfo/shared-memory",
                    "HWiNFO",
                    HardwareKind.Unknown,
                    "/hwinfo/shared-memory/unavailable",
                    "Shared memory unavailable",
                    SensorKind.Unknown,
                    string.Empty);
                return Task.FromResult<IReadOnlyList<RawSensorReading>>(
                [
                    new RawSensorReading(descriptor, null, now, false,
                        "HWiNFO is running, but Shared Memory Support is disabled or inaccessible. Enable it in HWiNFO Settings.")
                ]);
            }
            return Task.FromResult<IReadOnlyList<RawSensorReading>>([]);
        }
    }

    private RawSensorReading? Map(SensorReading reading, DateTimeOffset timestamp)
    {
        var sensorKind = reading.Type switch
        {
            SensorType.SensorTypeTemp => SensorKind.Temperature,
            SensorType.SensorTypePower => SensorKind.Power,
            SensorType.SensorTypeFan => SensorKind.Fan,
            SensorType.SensorTypeUsage => SensorKind.Load,
            SensorType.SensorTypeVolt => SensorKind.Voltage,
            SensorType.SensorTypeClock => SensorKind.Clock,
            SensorType.SensorTypeOther => SensorKind.Data,
            _ => SensorKind.Unknown
        };
        if (sensorKind == SensorKind.Unknown) return null;

        var hardwareName = FirstNonEmpty(reading.GroupLabelUser, reading.GroupLabelOrig, "HWiNFO sensor group");
        var sensorName = FirstNonEmpty(reading.LabelUser, reading.LabelOrig, $"Sensor {reading.Id}");
        var descriptor = new SensorDescriptor(
            Name,
            $"/hwinfo/group/{reading.GroupId:x8}/{reading.GroupInstanceId}",
            hardwareName,
            InferHardware(hardwareName, sensorName),
            $"/hwinfo/{reading.GroupId:x8}/{reading.GroupInstanceId}/{reading.Id:x8}/{reading.Index}",
            sensorName,
            sensorKind,
            reading.Unit ?? string.Empty);
        var available = double.IsFinite(reading.Value);
        return new RawSensorReading(descriptor, available ? reading.Value : null, timestamp, available,
            available ? null : "HWiNFO published a non-finite value.");
    }

    private static HardwareKind InferHardware(string hardwareName, string sensorName)
    {
        var value = $"{hardwareName} {sensorName}".ToLowerInvariant();
        if (value.Contains("nvidia") || value.Contains("geforce")) return HardwareKind.GpuNvidia;
        if (value.Contains("radeon") || value.Contains("amd gpu")) return HardwareKind.GpuAmd;
        if (value.Contains("intel") && value.Contains("gpu")) return HardwareKind.GpuIntel;
        if (value.Contains("cpu [") || value.Contains("ryzen") || value.Contains("central processor")) return HardwareKind.Cpu;
        if (value.Contains("memory") || value.Contains("physical memory")) return HardwareKind.Memory;
        if (value.Contains("s.m.a.r.t") || value.Contains("nvme") || value.Contains("drive") || value.Contains("storage")) return HardwareKind.Storage;
        if (value.Contains("motherboard") || value.Contains("nuvoton") || value.Contains("nct") || value.Contains("msi ")) return HardwareKind.Motherboard;
        return sensorName.Contains("fan", StringComparison.OrdinalIgnoreCase) ? HardwareKind.Controller : HardwareKind.Unknown;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static bool IsHwinfoRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("HWiNFO64");
            foreach (var process in processes) process.Dispose();
            return processes.Length > 0;
        }
        catch { return false; }
    }

    public ValueTask DisposeAsync()
    {
        _reader.Dispose();
        return ValueTask.CompletedTask;
    }
}
