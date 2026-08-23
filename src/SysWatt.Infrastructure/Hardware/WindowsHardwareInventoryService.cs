using System.Management;
using SysWatt.Core.Power;

namespace SysWatt.Infrastructure.Hardware;

/// <summary>Reads Windows' physical inventory. Results are cached because WMI enumeration is comparatively expensive.</summary>
public sealed class WindowsHardwareInventoryService : IHardwareInventoryService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HardwareInventorySnapshot _current = HardwareInventorySnapshot.Empty;

    public HardwareInventorySnapshot Current => _current;

    public async Task<HardwareInventorySnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_current.DetectedAt > DateTimeOffset.UtcNow.AddMinutes(-10)) return _current;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current.DetectedAt > DateTimeOffset.UtcNow.AddMinutes(-10)) return _current;
            _current = await Task.Run(Detect, cancellationToken).ConfigureAwait(false);
            return _current;
        }
        catch
        {
            // Inventory failure must never interrupt live telemetry. Manual model values remain available.
            return _current;
        }
        finally { _gate.Release(); }
    }

    private static HardwareInventorySnapshot Detect()
    {
        var system = First("SELECT Manufacturer, Model FROM Win32_ComputerSystem", item =>
            $"{Text(item, "Manufacturer")} {Text(item, "Model")}".Trim());
        var board = First("SELECT Manufacturer, Product FROM Win32_BaseBoard", item =>
            $"{Text(item, "Manufacturer")} {Text(item, "Product")}".Trim());
        var storage = Query("SELECT Model, MediaType, InterfaceType, PNPDeviceID FROM Win32_DiskDrive")
            .Select(item => CreateStorage(
                Text(item, "PNPDeviceID"), Text(item, "Model"), Text(item, "MediaType"), Text(item, "InterfaceType")))
            .ToArray();
        var displays = Query("SELECT Status FROM Win32_DesktopMonitor")
            .Count(item => string.Equals(Text(item, "Status"), "OK", StringComparison.OrdinalIgnoreCase));
        var removable = Query("SELECT PNPClass, Status FROM Win32_PnPEntity")
            .Count(item => string.Equals(Text(item, "Status"), "OK", StringComparison.OrdinalIgnoreCase) &&
                Text(item, "PNPClass") is "Camera" or "Image" or "PortableDevice");
        return new(
            string.IsNullOrWhiteSpace(system) ? "Windows PC" : system,
            string.IsNullOrWhiteSpace(board) ? "Motherboard not reported" : board,
            storage,
            displays,
            removable,
            DateTimeOffset.UtcNow);
    }

    internal static StoragePowerProfile CreateStorage(string id, string model, string mediaType, string interfaceType)
    {
        var identity = $"{id} {model} {mediaType} {interfaceType}".ToUpperInvariant();
        var type = identity.Contains("USB") || identity.Contains("REMOVABLE")
            ? StorageDeviceClass.Removable
            : identity.Contains("NVME")
                ? StorageDeviceClass.Nvme
                : identity.Contains("SSD") || identity.Contains("SOLID STATE")
                    ? StorageDeviceClass.SolidState
                    : identity.Contains("FIXED HARD DISK") || identity.Contains(" IDE")
                        ? StorageDeviceClass.HardDisk
                        : StorageDeviceClass.Unknown;
        var (idle, active) = type switch
        {
            StorageDeviceClass.Nvme => (0.8, 6.0),
            StorageDeviceClass.SolidState => (0.5, 3.0),
            StorageDeviceClass.HardDisk => (3.8, 8.0),
            StorageDeviceClass.Removable => (1.0, 4.0),
            _ => (1.0, 5.0)
        };
        return new(string.IsNullOrWhiteSpace(id) ? model : id, string.IsNullOrWhiteSpace(model) ? "Unknown storage device" : model, type, idle, active);
    }

    private static string First(string query, Func<ManagementObject, string> selector) =>
        Query(query).Select(selector).FirstOrDefault() ?? string.Empty;

    private static IReadOnlyList<ManagementObject> Query(string query)
    {
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        return results.Cast<ManagementObject>().ToArray();
    }

    private static string Text(ManagementBaseObject item, string name) => item[name]?.ToString()?.Trim() ?? string.Empty;
}
