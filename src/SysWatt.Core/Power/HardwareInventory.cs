namespace SysWatt.Core.Power;

public enum StorageDeviceClass { Nvme, SolidState, HardDisk, Removable, Unknown }

public sealed record StoragePowerProfile(
    string DeviceId,
    string Name,
    StorageDeviceClass DeviceClass,
    double IdleWatts,
    double ActiveWatts,
    bool IsManual = false);

public sealed record HardwareInventorySnapshot(
    string SystemName,
    string Motherboard,
    IReadOnlyList<StoragePowerProfile> StorageDevices,
    int ActiveDisplayCount,
    int RemovablePeripheralCount,
    DateTimeOffset DetectedAt)
{
    public static HardwareInventorySnapshot Empty { get; } = new(
        "Unknown system", "Unknown motherboard", [], 0, 0, DateTimeOffset.MinValue);

    public string StorageSummary => StorageDevices.Count == 0
        ? "No physical storage inventory was returned by Windows."
        : string.Join(" · ", StorageDevices
            .GroupBy(device => device.DeviceClass)
            .Select(group => $"{group.Count()} {DisplayName(group.Key)}"));

    private static string DisplayName(StorageDeviceClass value) => value switch
    {
        StorageDeviceClass.Nvme => "NVMe",
        StorageDeviceClass.SolidState => "SSD",
        StorageDeviceClass.HardDisk => "HDD",
        StorageDeviceClass.Removable => "removable",
        _ => "unknown"
    };
}

public interface IHardwareInventoryService
{
    HardwareInventorySnapshot Current { get; }
    Task<HardwareInventorySnapshot> GetAsync(CancellationToken cancellationToken = default);
}
