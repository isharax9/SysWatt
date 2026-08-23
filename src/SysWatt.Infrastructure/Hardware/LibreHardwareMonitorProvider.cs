using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using SysWatt.Core.Sensors;

namespace SysWatt.Infrastructure.Hardware;

public sealed class LibreHardwareMonitorProvider : IRawSensorProvider
{
    private readonly Computer _computer;
    private readonly ILogger<LibreHardwareMonitorProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _opened;

    public string Name => "LibreHardwareMonitor";

    public LibreHardwareMonitorProvider(ILogger<LibreHardwareMonitorProvider> logger)
    {
        _logger = logger;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = false,
            IsPsuEnabled = true
        };
    }

    public async Task<IReadOnlyList<RawSensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureOpen();
            var now = DateTimeOffset.UtcNow;
            var readings = new List<RawSensorReading>();
            foreach (var hardware in _computer.Hardware)
            {
                ReadHardwareRecursive(hardware, readings, now, cancellationToken);
            }
            return readings;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureOpen()
    {
        if (_opened) return;
        _computer.Open();
        _opened = true;
        _logger.LogInformation("LibreHardwareMonitor opened with dynamic hardware discovery.");
    }

    private void ReadHardwareRecursive(IHardware hardware, List<RawSensorReading> destination, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                var descriptor = new SensorDescriptor(
                    Name,
                    hardware.Identifier.ToString(),
                    hardware.Name,
                    MapHardware(hardware.HardwareType),
                    sensor.Identifier.ToString(),
                    sensor.Name,
                    MapSensor(sensor.SensorType),
                    UnitFor(sensor.SensorType));
                destination.Add(new RawSensorReading(descriptor, sensor.Value, now, sensor.Value.HasValue));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unable to update hardware {HardwareId} ({HardwareType}); continuing with other devices.",
                hardware.Identifier, hardware.HardwareType);
            var descriptor = new SensorDescriptor(
                Name,
                hardware.Identifier.ToString(),
                hardware.Name,
                MapHardware(hardware.HardwareType),
                $"{hardware.Identifier}/update-error",
                "Hardware update failure",
                SensorKind.Unknown,
                string.Empty);
            destination.Add(new RawSensorReading(descriptor, null, now, false, ex.Message));
        }

        foreach (var child in hardware.SubHardware)
        {
            ReadHardwareRecursive(child, destination, now, cancellationToken);
        }
    }

    private static HardwareKind MapHardware(HardwareType value) => value switch
    {
        HardwareType.Cpu => HardwareKind.Cpu,
        HardwareType.GpuNvidia => HardwareKind.GpuNvidia,
        HardwareType.GpuAmd => HardwareKind.GpuAmd,
        HardwareType.GpuIntel => HardwareKind.GpuIntel,
        HardwareType.Memory => HardwareKind.Memory,
        HardwareType.Storage => HardwareKind.Storage,
        HardwareType.Motherboard => HardwareKind.Motherboard,
        HardwareType.SuperIO or HardwareType.EmbeddedController => HardwareKind.Controller,
        HardwareType.Network => HardwareKind.Network,
        _ => HardwareKind.Unknown
    };

    private static SensorKind MapSensor(SensorType value) => value switch
    {
        SensorType.Load => SensorKind.Load,
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Power => SensorKind.Power,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Data => SensorKind.Data,
        SensorType.Throughput => SensorKind.Throughput,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Control => SensorKind.Control,
        _ => SensorKind.Unknown
    };

    private static string UnitFor(SensorType value) => value switch
    {
        SensorType.Load => "%", SensorType.Temperature => "°C", SensorType.Power => "W",
        SensorType.Fan => "RPM", SensorType.Clock => "MHz", SensorType.Voltage => "V",
        SensorType.Control => "%", SensorType.Data => "GB", SensorType.Throughput => "B/s", _ => string.Empty
    };

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_opened) _computer.Close();
            _opened = false;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
