namespace SysWatt.Core.Power;

public sealed record PowerModelSettings(double BaseSystemWatts = 45, double PsuEfficiency = 0.87)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (BaseSystemWatts is < 0 or > 1000) errors.Add("Base system consumption must be between 0 and 1000 W.");
        if (PsuEfficiency is < 0.50 or > 1.0) errors.Add("PSU efficiency must be between 50% and 100%.");
        return errors;
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

        var dc = settings.BaseSystemWatts + (cpuWatts ?? 0) + (gpuWatts ?? 0);
        var partial = !cpuWatts.HasValue || !gpuWatts.HasValue;
        return new PowerEstimate(
            Math.Round(dc, 1),
            Math.Round(dc / settings.PsuEfficiency, 1),
            partial,
            partial ? "Partial estimate: one or more component power sensors are unavailable." : "Estimate uses measured CPU/GPU sensor values plus the configured unmeasured base load.",
            "Estimated DC = CPU + GPU + base system; estimated wall draw = DC / PSU efficiency.");
    }
}
