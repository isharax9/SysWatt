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
    double OtherWallWatts = 0)
{
    public double FanWatts => FanCount * WattsPerFan;
    public double PcAuxiliaryWatts => BaseSystemWatts + StorageWatts + FanWatts + OtherCoolingWatts + UsbPeripheralWatts;
    public double ExternalAcWatts => DisplayWatts + ExternalPeripheralWatts + OtherWallWatts;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidateWatts(BaseSystemWatts, "Motherboard and memory", errors);
        ValidateWatts(StorageWatts, "Storage", errors);
        if (FanCount is < 0 or > 100) errors.Add("Fan count must be between 0 and 100.");
        ValidateWatts(WattsPerFan, "Watts per fan", errors, 100);
        ValidateWatts(OtherCoolingWatts, "Other cooling", errors);
        ValidateWatts(UsbPeripheralWatts, "USB peripherals", errors);
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
}

public sealed record PowerEstimate(
    double EstimatedDcWatts,
    double EstimatedWallWatts,
    bool IsPartial,
    string Confidence,
    string Formula);

public interface IPowerEstimationService
{
    PowerEstimate Calculate(double? cpuWatts, double? gpuWatts, PowerModelSettings settings);
}

public sealed class PowerEstimationService : IPowerEstimationService
{
    public PowerEstimate Calculate(double? cpuWatts, double? gpuWatts, PowerModelSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0) throw new ArgumentOutOfRangeException(nameof(settings), string.Join(" ", errors));
        if (cpuWatts is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(cpuWatts));
        if (gpuWatts is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(gpuWatts));

        var dc = settings.PcAuxiliaryWatts + (cpuWatts ?? 0) + (gpuWatts ?? 0);
        var wall = (dc / settings.PsuEfficiency) + settings.ExternalAcWatts;
        var partial = !cpuWatts.HasValue || !gpuWatts.HasValue;
        return new PowerEstimate(
            Math.Round(dc, 1),
            Math.Round(wall, 1),
            partial,
            partial ? "Partial estimate: one or more component power sensors are unavailable." : "Estimate uses measured CPU/GPU values plus configured PC and external loads.",
            $"PC DC = CPU + GPU + {settings.PcAuxiliaryWatts:0.#} W auxiliaries " +
            $"({settings.FanCount} fans = {settings.FanWatts:0.#} W); setup wall = PC DC / {settings.PsuEfficiency:P0} + {settings.ExternalAcWatts:0.#} W displays/external.");
    }
}
