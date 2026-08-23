using SysWatt.Core.Sensors;

namespace SysWatt.Core.Energy;

public sealed record DailyEnergySummary(DateOnly Date, double KilowattHours, double AverageWatts, double PeakWatts)
{
    public bool HasData { get; init; }
    public IReadOnlyDictionary<TelemetrySource, double> KilowattHoursBySource { get; init; } =
        new Dictionary<TelemetrySource, double>();

    public string SourceSummary => KilowattHoursBySource.Count == 0
        ? "No source samples yet"
        : string.Join(" · ", KilowattHoursBySource
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key switch
            {
                TelemetrySource.HWiNFOBridge => "HWiNFO",
                TelemetrySource.FullHardwareAccess => "Hardware",
                TelemetrySource.HybridModel => "Hybrid model",
                TelemetrySource.Imported => "Imported",
                _ => "Standalone"
            }} {pair.Value:0.000} kWh"));
}

public interface IEnergyHistoryStore : IAsyncDisposable
{
    string DatabasePath { get; }
    Task RecordSampleAsync(DateTimeOffset timestamp, double watts, CancellationToken cancellationToken = default);
    Task RecordSampleAsync(DateTimeOffset timestamp, double watts, TelemetrySource source, CancellationToken cancellationToken = default);
    Task<DailyEnergySummary> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyEnergySummary>> GetRangeAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default);
    Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<int> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
}
