using System.ComponentModel;
using System.Runtime.InteropServices;
using SysWatt.Core.Sensors;

namespace SysWatt.Infrastructure.Hardware;

/// <summary>Reads Windows' own CPU and physical-disk counters; no companion monitoring app is required.</summary>
public sealed class WindowsPerformanceProvider : IRawSensorProvider
{
    private const uint PdhFormatDouble = 0x00000200;
    private readonly object _gate = new();
    private IntPtr _query;
    private IntPtr _diskActivity;
    private IntPtr _diskRead;
    private IntPtr _diskWrite;
    private ulong? _previousIdle;
    private ulong? _previousKernel;
    private ulong? _previousUser;
    private string? _pdhError;

    public string Name => "Windows Native Telemetry";

    public WindowsPerformanceProvider()
    {
        try
        {
            CheckPdh(PdhOpenQuery(null, IntPtr.Zero, out _query), "open the Windows performance query");
            CheckPdh(PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\% Disk Time", IntPtr.Zero, out _diskActivity), "add disk activity");
            CheckPdh(PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out _diskRead), "add disk read rate");
            CheckPdh(PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out _diskWrite), "add disk write rate");
            PdhCollectQueryData(_query); // PDH needs a baseline before rates become valid.
        }
        catch (Exception ex)
        {
            _pdhError = ex.Message;
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    public Task<IReadOnlyList<RawSensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var readings = new List<RawSensorReading>(4) { ReadCpu(now) };
            if (_query == IntPtr.Zero)
            {
                readings.Add(Unavailable("windows/storage/activity", "Total activity", SensorKind.Load, "%", now, _pdhError ?? "Windows disk counters are unavailable."));
                return Task.FromResult<IReadOnlyList<RawSensorReading>>(readings);
            }

            var status = PdhCollectQueryData(_query);
            if (status != 0)
            {
                readings.Add(Unavailable("windows/storage/activity", "Total activity", SensorKind.Load, "%", now, $"Windows disk counter collection failed (0x{status:X8})."));
                return Task.FromResult<IReadOnlyList<RawSensorReading>>(readings);
            }

            readings.Add(ReadCounter(_diskActivity, "windows/storage/activity", "Total activity", SensorKind.Load, "%", now, value => Math.Clamp(value, 0, 100)));
            readings.Add(ReadCounter(_diskRead, "windows/storage/read", "Disk read rate", SensorKind.Throughput, "MB/s", now, BytesToMegabytes));
            readings.Add(ReadCounter(_diskWrite, "windows/storage/write", "Disk write rate", SensorKind.Throughput, "MB/s", now, BytesToMegabytes));
            return Task.FromResult<IReadOnlyList<RawSensorReading>>(readings);
        }
    }

    private RawSensorReading ReadCpu(DateTimeOffset now)
    {
        var descriptor = Descriptor("windows/cpu/load", "CPU total", HardwareKind.Cpu, SensorKind.Load, "%");
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return new(descriptor, null, now, false, new Win32Exception(Marshal.GetLastWin32Error()).Message);

        var idleValue = ToUInt64(idle);
        var kernelValue = ToUInt64(kernel);
        var userValue = ToUInt64(user);
        double? usage = null;
        if (_previousIdle.HasValue)
        {
            var idleDelta = idleValue - _previousIdle.Value;
            var totalDelta = (kernelValue - _previousKernel!.Value) + (userValue - _previousUser!.Value);
            if (totalDelta > 0) usage = Math.Clamp(100d * (totalDelta - idleDelta) / totalDelta, 0, 100);
        }
        _previousIdle = idleValue;
        _previousKernel = kernelValue;
        _previousUser = userValue;
        return new(descriptor, usage, now, usage.HasValue, usage.HasValue ? null : "Collecting the initial Windows CPU baseline.");
    }

    private RawSensorReading ReadCounter(IntPtr counter, string id, string name, SensorKind kind, string unit,
        DateTimeOffset now, Func<double, double> convert)
    {
        var status = PdhGetFormattedCounterValue(counter, PdhFormatDouble, out _, out var value);
        if (status != 0 || value.Status > 1 || !double.IsFinite(value.DoubleValue))
            return Unavailable(id, name, kind, unit, now, "Collecting the initial Windows performance-counter baseline.");
        return new(Descriptor(id, name, HardwareKind.Storage, kind, unit), convert(value.DoubleValue), now);
    }

    private RawSensorReading Unavailable(string id, string name, SensorKind kind, string unit, DateTimeOffset now, string error) =>
        new(Descriptor(id, name, HardwareKind.Storage, kind, unit), null, now, false, error);

    private SensorDescriptor Descriptor(string id, string name, HardwareKind hardware, SensorKind kind, string unit) =>
        new(Name, hardware == HardwareKind.Cpu ? "windows/cpu" : "windows/storage", hardware == HardwareKind.Cpu ? "Windows CPU" : "Physical disks",
            hardware, id, name, kind, unit);

    private static double BytesToMegabytes(double value) => Math.Max(0, value / 1_048_576d);
    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;
    private static void CheckPdh(uint status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"Could not {operation} (PDH 0x{status:X8}).");
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
        return ValueTask.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFormattedCounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFormattedCounterValue value);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
