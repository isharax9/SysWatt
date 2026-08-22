using System.Runtime.InteropServices;
using SysWatt.Core.Sensors;

namespace SysWatt.Infrastructure.Hardware;

public sealed class WindowsMemoryProvider : IRawSensorProvider
{
    public string Name => "WindowsMemory";

    public Task<IReadOnlyList<RawSensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) throw new InvalidOperationException($"GlobalMemoryStatusEx failed with {Marshal.GetLastWin32Error()}.");
        var descriptor = new SensorDescriptor(Name, "windows/memory", "System memory", HardwareKind.Memory,
            "windows/memory/load", "Memory used", SensorKind.Load, "%");
        IReadOnlyList<RawSensorReading> result = [new(descriptor, status.MemoryLoad, DateTimeOffset.UtcNow)];
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
