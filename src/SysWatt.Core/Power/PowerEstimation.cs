namespace SysWatt.Core.Power;

public sealed record PowerModelSettings(
    double BaseSystemWatts = 30,
    double PsuEfficiency = 0.87,
    double StorageWatts = 8,
    int FanCount = 3,
    double WattsPerFan = 2,
    double OtherCoolingWatts = 0,
    double UsbPeripheralWatts = 5,
    double DisplayWatts = 0,
    double ExternalPeripheralWatts = 0,
    double OtherWallWatts = 0,
    double CpuIdleWatts = 22,
    double CpuPeakWatts = 125,
    double GpuIdleWatts = 7,
    double GpuPeakWatts = 220,
    int StorageDeviceCount = 1,
    double StorageIdleWattsPerDevice = 0.8,
    double StorageThroughputCeilingMBps = 500,
    bool AutoDetectStorage = true,
    bool AutoDetectCooling = true,
    bool AutoDetectDisplays = true,
    int DisplayCount = 1,
    bool AutoDetectRemovablePeripherals = true,
    double WattsPerDetectedPeripheral = 2.5)
{
    public double FanWatts => FanCount * WattsPerFan;
    public double PcAuxiliaryWatts => BaseSystemWatts + FanWatts + OtherCoolingWatts + UsbPeripheralWatts;
    public double ExternalAcWatts => DisplayWatts + ExternalPeripheralWatts + OtherWallWatts;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidateWatts(BaseSystemWatts, "Motherboard and memory", errors);
        ValidateWatts(StorageWatts, "Storage", errors);
        ValidateWatts(CpuIdleWatts, "CPU idle estimate", errors, 1000);
        ValidateWatts(CpuPeakWatts, "CPU peak estimate", errors, 2000);
        ValidateWatts(GpuIdleWatts, "GPU idle estimate", errors, 1000);
        ValidateWatts(GpuPeakWatts, "GPU peak estimate", errors, 2000);
        if (CpuPeakWatts < CpuIdleWatts) errors.Add("CPU peak estimate must be at least the CPU idle estimate.");
        if (GpuPeakWatts < GpuIdleWatts) errors.Add("GPU peak estimate must be at least the GPU idle estimate.");
        if (StorageDeviceCount is < 0 or > 32) errors.Add("Storage device count must be between 0 and 32.");
        ValidateWatts(StorageIdleWattsPerDevice, "Storage idle watts per device", errors, 100);
        if (!double.IsFinite(StorageThroughputCeilingMBps) || StorageThroughputCeilingMBps is < 1 or > 100_000)
            errors.Add("Storage throughput ceiling must be between 1 and 100,000 MB/s.");
        if (FanCount is < 0 or > 100) errors.Add("Fan count must be between 0 and 100.");
        if (DisplayCount is < 0 or > 32) errors.Add("Display count must be between 0 and 32.");
        ValidateWatts(WattsPerFan, "Watts per fan", errors, 100);
        ValidateWatts(OtherCoolingWatts, "Other cooling", errors);
        ValidateWatts(UsbPeripheralWatts, "USB peripherals", errors);
        ValidateWatts(WattsPerDetectedPeripheral, "Detected peripheral watts", errors, 100);
        ValidateWatts(DisplayWatts, "Displays", errors, 5000);
        ValidateWatts(ExternalPeripheralWatts, "External peripherals", errors, 5000);
        ValidateWatts(OtherWallWatts, "Other wall loads", errors, 5000);
        if (PsuEfficiency is < 0.50 or > 1.0) errors.Add("PSU efficiency must be between 50% and 100%.");
        return errors;
    }

    private static void ValidateWatts(double value, string label, List<string> errors, double maximum = 1000)
    {
        if (!double.IsFinite(value) || value < 0 || value > maximum)
            errors.Add($"{label} consumption must be between 0 and {maximum:0} W.");
    }

    public PowerModelSettings ApplyInventory(HardwareInventorySnapshot inventory, int detectedCoolingHeaders)
    {
        var storage = AutoDetectStorage && inventory.StorageDevices.Count > 0 ? inventory.StorageDevices : null;
        var storageCount = storage?.Count ?? StorageDeviceCount;
        var storageIdle = storageCount == 0 ? 0 : storage?.Average(device => device.IdleWatts) ?? StorageIdleWattsPerDevice;
        var storagePeak = storage?.Sum(device => device.ActiveWatts) ?? StorageWatts;
        // A fan header can feed a splitter, so auto-detection is a safe lower bound. The manual count remains an override/minimum.
        var fans = AutoDetectCooling ? Math.Max(FanCount, detectedCoolingHeaders) : FanCount;
        var displays = AutoDetectDisplays && inventory.ActiveDisplayCount > 0 ? inventory.ActiveDisplayCount : DisplayCount;
        var detectedPeripheralWatts = AutoDetectRemovablePeripherals
            ? inventory.RemovablePeripheralCount * WattsPerDetectedPeripheral
            : 0;
        return this with
        {
            StorageDeviceCount = storageCount,
            StorageIdleWattsPerDevice = storageIdle,
            StorageWatts = storagePeak,
            FanCount = fans,
            DisplayWatts = DisplayWatts * displays,
            DisplayCount = displays,
            UsbPeripheralWatts = UsbPeripheralWatts + detectedPeripheralWatts
        };
    }
}

public sealed record PowerEstimate(
    double EstimatedDcWatts,
    double EstimatedWallWatts,
    double EffectiveCpuWatts,
    double EffectiveGpuWatts,
    double EffectiveStorageWatts,
    bool CpuIsModeled,
    bool GpuIsModeled,
    bool IsPartial,
    string Confidence,
    string Formula)
{
    public double BaseSystemWatts { get; init; }
    public double CoolingWatts { get; init; }
    public double PeripheralDcWatts { get; init; }
    public double ExternalAcWatts { get; init; }
    public bool StorageIsModeled { get; init; }
}

public interface IPowerEstimationService
{
    PowerEstimate Calculate(double? cpuWatts, double? gpuWatts, PowerModelSettings settings,
        double? cpuUsage = null, double? gpuUsage = null, double? storageActivity = null,
        double? storageReadMBps = null, double? storageWriteMBps = null, double? storageWatts = null);
}

public sealed class PowerEstimationService : IPowerEstimationService
{
    public PowerEstimate Calculate(double? cpuWatts, double? gpuWatts, PowerModelSettings settings,
        double? cpuUsage = null, double? gpuUsage = null, double? storageActivity = null,
        double? storageReadMBps = null, double? storageWriteMBps = null, double? storageWatts = null)
    {
        var errors = settings.Validate();
        if (errors.Count > 0) throw new ArgumentOutOfRangeException(nameof(settings), string.Join(" ", errors));
        if (cpuWatts is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(cpuWatts));
        if (gpuWatts is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(gpuWatts));
        if (storageWatts is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(storageWatts));

        var effectiveCpu = cpuWatts ?? ModelComponent(cpuUsage, settings.CpuIdleWatts, settings.CpuPeakWatts);
        var effectiveGpu = gpuWatts ?? ModelComponent(gpuUsage, settings.GpuIdleWatts, settings.GpuPeakWatts);
        var storage = storageWatts ?? ModelStorage(settings, storageActivity, storageReadMBps, storageWriteMBps);
        var dc = settings.PcAuxiliaryWatts + effectiveCpu + effectiveGpu + storage;
        var wall = (dc / settings.PsuEfficiency) + settings.ExternalAcWatts;
        var partial = !cpuWatts.HasValue || !gpuWatts.HasValue;
        return new PowerEstimate(
            Math.Round(dc, 1),
            Math.Round(wall, 1),
            Math.Round(effectiveCpu, 1),
            Math.Round(effectiveGpu, 1),
            Math.Round(storage, 1),
            !cpuWatts.HasValue,
            !gpuWatts.HasValue,
            partial,
            partial ? "Hybrid estimate: unavailable component power is modeled from live utilization." : "Measured CPU/GPU power with activity-aware storage and configured system loads.",
            $"PC DC = CPU + GPU + {storage:0.#} W storage + {settings.PcAuxiliaryWatts:0.#} W auxiliaries " +
            $"({settings.FanCount} fans = {settings.FanWatts:0.#} W); setup wall = PC DC / {settings.PsuEfficiency:P0} + {settings.ExternalAcWatts:0.#} W displays/external.")
        {
            BaseSystemWatts = settings.BaseSystemWatts,
            CoolingWatts = settings.FanWatts + settings.OtherCoolingWatts,
            PeripheralDcWatts = settings.UsbPeripheralWatts,
            ExternalAcWatts = settings.ExternalAcWatts,
            StorageIsModeled = !storageWatts.HasValue
        };
    }

    private static double ModelComponent(double? usage, double idle, double peak)
    {
        var normalized = Math.Clamp((usage ?? 0) / 100d, 0, 1);
        return idle + ((peak - idle) * Math.Pow(normalized, 1.35));
    }

    private static double ModelStorage(PowerModelSettings settings, double? activity, double? read, double? write)
    {
        if (settings.StorageDeviceCount == 0 || settings.StorageWatts <= 0) return 0;
        if (!activity.HasValue && !read.HasValue && !write.HasValue) return settings.StorageWatts;
        var idle = settings.StorageDeviceCount * settings.StorageIdleWattsPerDevice;
        var activityFactor = Math.Clamp((activity ?? 0) / 100d, 0, 1);
        var throughputFactor = Math.Clamp(((read ?? 0) + (write ?? 0)) / settings.StorageThroughputCeilingMBps, 0, 1);
        var demand = Math.Max(activityFactor, throughputFactor);
        return idle + (Math.Max(idle, settings.StorageWatts) - idle) * Math.Pow(demand, 0.7);
    }
}
