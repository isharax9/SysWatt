using System.IO.MemoryMappedFiles;
using System.Text;
using SysWatt.Core.Sensors;

namespace SysWatt.Infrastructure.Hardware;

/// <summary>
/// Reads HWiNFO's public Sensors Shared Memory Interface v2. HWiNFO remains an
/// optional bridge: when its mapping is absent or inactive this provider emits
/// one diagnostic reading and the other SysWatt providers continue normally.
/// </summary>
public sealed class HWiNFOSharedMemoryProvider : IRawSensorProvider
{
    private const string MapName = "Global\\HWiNFO_SENS_SM2";
    private const string MutexName = "Global\\HWiNFO_SM2_MUTEX";
    private const uint ActiveSignature = 0x53695748; // bytes: H W i S
    private const int HeaderSize = 48;
    private const int SensorElementSize = 264;
    private const int ReadingElementSize = 316;

    public string Name => "HWiNFO Shared Memory";

    public Task<IReadOnlyList<RawSensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        try
        {
            using var mapping = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            using var accessor = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            using var mutex = TryOpenMutex();
            var mutexAcquired = false;
            if (mutex is not null)
            {
                try { mutexAcquired = mutex.WaitOne(100); } catch (AbandonedMutexException) { mutexAcquired = true; }
            }

            try
            {
                var bytes = new byte[checked((int)accessor.Capacity)];
                accessor.ReadArray(0, bytes, 0, bytes.Length);
                var result = Parse(bytes, now);
                return Task.FromResult<IReadOnlyList<RawSensorReading>>(result);
            }
            finally
            {
                if (mutexAcquired) mutex!.ReleaseMutex();
            }
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<IReadOnlyList<RawSensorReading>>([Unavailable(now,
                "HWiNFO is not running or Shared Memory Support is disabled.")]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Task.FromResult<IReadOnlyList<RawSensorReading>>([Unavailable(now,
                $"HWiNFO shared memory could not be read: {ex.Message}")]);
        }
    }

    private IReadOnlyList<RawSensorReading> Parse(byte[] bytes, DateTimeOffset now)
    {
        if (bytes.Length < HeaderSize)
            return [Unavailable(now, "HWiNFO shared memory is smaller than the supported v2 header.")];

        var signature = BitConverter.ToUInt32(bytes, 0);
        if (signature != ActiveSignature)
            return [Unavailable(now, "HWiNFO Shared Memory Support is inactive. Open the Sensors window and enable Shared Memory Support.")];

        var sensorOffset = (int)BitConverter.ToUInt32(bytes, 20);
        var sensorSize = (int)BitConverter.ToUInt32(bytes, 24);
        var sensorCount = (int)BitConverter.ToUInt32(bytes, 28);
        var readingOffset = (int)BitConverter.ToUInt32(bytes, 32);
        var readingSize = (int)BitConverter.ToUInt32(bytes, 36);
        var readingCount = (int)BitConverter.ToUInt32(bytes, 40);
        if (sensorSize < SensorElementSize || readingSize < ReadingElementSize ||
            !IsSectionValid(bytes, sensorOffset, sensorSize, sensorCount) ||
            !IsSectionValid(bytes, readingOffset, readingSize, readingCount))
            return [Unavailable(now, "HWiNFO shared memory has an unsupported layout revision.")];

        var sensors = new Dictionary<int, SensorInfo>();
        for (var index = 0; index < sensorCount; index++)
        {
            var offset = sensorOffset + index * sensorSize;
            var id = BitConverter.ToUInt32(bytes, offset);
            var instance = BitConverter.ToUInt32(bytes, offset + 4);
            var original = ReadString(bytes, offset + 8, 128);
            var custom = ReadString(bytes, offset + 136, 128);
            sensors[index] = new(id, instance, string.IsNullOrWhiteSpace(custom) ? original : custom,
                InferHardwareKind($"{original} {custom}"));
        }

        var result = new List<RawSensorReading>(readingCount);
        for (var index = 0; index < readingCount; index++)
        {
            var offset = readingOffset + index * readingSize;
            var type = BitConverter.ToUInt32(bytes, offset);
            var sensorIndex = (int)BitConverter.ToUInt32(bytes, offset + 4);
            var readingId = BitConverter.ToUInt32(bytes, offset + 8);
            if (!sensors.TryGetValue(sensorIndex, out var sensor)) continue;
            var sensorKind = MapSensorKind(type);
            if (sensorKind == SensorKind.Unknown) continue;
            var original = ReadString(bytes, offset + 12, 128);
            var custom = ReadString(bytes, offset + 140, 128);
            var name = string.IsNullOrWhiteSpace(custom) ? original : custom;
            var unit = ReadString(bytes, offset + 268, 16);
            var value = BitConverter.ToDouble(bytes, offset + 284);
            var descriptor = new SensorDescriptor(Name,
                $"hwinfo/{sensor.Id:X8}/{sensor.Instance:X8}", sensor.Name, sensor.Kind,
                $"hwinfo/{sensor.Id:X8}/{sensor.Instance:X8}/{readingId:X8}", name, sensorKind, unit);
            result.Add(new RawSensorReading(descriptor, double.IsFinite(value) ? value : null, now,
                double.IsFinite(value), double.IsFinite(value) ? null : "HWiNFO returned a non-finite sensor value."));
        }

        return result.Count == 0
            ? [Unavailable(now, "HWiNFO is connected but has not published any compatible sensor readings.")]
            : result;
    }

    private RawSensorReading Unavailable(DateTimeOffset now, string error)
    {
        var descriptor = new SensorDescriptor(Name, "hwinfo/status", "HWiNFO Shared Memory", HardwareKind.Unknown,
            "hwinfo/status", "Bridge status", SensorKind.Unknown, string.Empty);
        return new RawSensorReading(descriptor, null, now, false, error);
    }

    private static bool IsSectionValid(byte[] bytes, int offset, int size, int count) =>
        offset >= HeaderSize && size > 0 && count >= 0 && count <= 100_000 &&
        offset <= bytes.Length && count <= (bytes.Length - offset) / size;

    private static string ReadString(byte[] bytes, int offset, int length)
    {
        var end = offset;
        var limit = Math.Min(bytes.Length, offset + length);
        while (end < limit && bytes[end] != 0) end++;
        return Encoding.UTF8.GetString(bytes, offset, end - offset).Trim();
    }

    private static SensorKind MapSensorKind(uint type) => type switch
    {
        1 => SensorKind.Temperature,
        2 => SensorKind.Voltage,
        3 => SensorKind.Fan,
        4 => SensorKind.Unknown, // Current is not a canonical SysWatt metric yet.
        5 => SensorKind.Power,
        6 => SensorKind.Clock,
        7 => SensorKind.Load,
        _ => SensorKind.Unknown
    };

    private static HardwareKind InferHardwareKind(string text)
    {
        if (text.Contains("GPU", StringComparison.OrdinalIgnoreCase) || text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return HardwareKind.GpuNvidia;
        if (text.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || text.Contains("AMD Graphics", StringComparison.OrdinalIgnoreCase))
            return HardwareKind.GpuAmd;
        if (text.Contains("Intel Arc", StringComparison.OrdinalIgnoreCase)) return HardwareKind.GpuIntel;
        if (text.Contains("CPU", StringComparison.OrdinalIgnoreCase) || text.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Core(TM)", StringComparison.OrdinalIgnoreCase)) return HardwareKind.Cpu;
        if (text.Contains("Drive", StringComparison.OrdinalIgnoreCase) || text.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || text.Contains("HDD", StringComparison.OrdinalIgnoreCase)) return HardwareKind.Storage;
        if (text.Contains("Memory", StringComparison.OrdinalIgnoreCase) || text.Contains("DIMM", StringComparison.OrdinalIgnoreCase)) return HardwareKind.Memory;
        if (text.Contains("Nuvoton", StringComparison.OrdinalIgnoreCase) || text.Contains("ITE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Super I/O", StringComparison.OrdinalIgnoreCase)) return HardwareKind.Controller;
        return HardwareKind.Motherboard;
    }

    private static Mutex? TryOpenMutex()
    {
        try { return Mutex.OpenExisting(MutexName); }
        catch (WaitHandleCannotBeOpenedException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record SensorInfo(uint Id, uint Instance, string Name, HardwareKind Kind);
}
