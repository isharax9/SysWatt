namespace SysWatt.Core.Energy;

public sealed record DailyEnergySummary(DateOnly Date, double KilowattHours, double AverageWatts, double PeakWatts);

public interface IEnergyHistoryStore : IAsyncDisposable
{
    string DatabasePath { get; }
    Task RecordSampleAsync(DateTimeOffset timestamp, double watts, CancellationToken cancellationToken = default);
    Task<DailyEnergySummary> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyEnergySummary>> GetRangeAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default);
}
