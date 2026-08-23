using SysWatt.Infrastructure.Energy;
using SysWatt.Core.Sensors;

namespace SysWatt.Infrastructure.Tests;

public sealed class SqliteEnergyHistoryStoreTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SysWatt-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task IntegratesWattsIntoDailyKilowattHours()
    {
        await using var store = new SqliteEnergyHistoryStore(_root);
        var start = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

        var cancellationToken = TestContext.Current.CancellationToken;
        await store.RecordSampleAsync(start, 1000, cancellationToken);
        await store.RecordSampleAsync(start.AddMinutes(1), 1000, cancellationToken);

        var day = await store.GetDayAsync(DateOnly.FromDateTime(start.LocalDateTime), cancellationToken);
        Assert.Equal(1d / 60d, day.KilowattHours, 6);
        Assert.Equal(1000, day.AverageWatts);
        Assert.Equal(1000, day.PeakWatts);
        Assert.True(File.Exists(store.DatabasePath));
    }

    [Fact]
    public async Task DoesNotCountLongOfflineGaps()
    {
        await using var store = new SqliteEnergyHistoryStore(_root);
        var start = DateTimeOffset.Now;
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.RecordSampleAsync(start, 500, cancellationToken);
        await store.RecordSampleAsync(start.AddHours(2), 500, cancellationToken);

        var day = await store.GetDayAsync(DateOnly.FromDateTime(start.LocalDateTime), cancellationToken);
        Assert.Equal(0, day.KilowattHours);
    }

    [Fact]
    public async Task RetainsTheSourceForEachIntegratedInterval()
    {
        await using var store = new SqliteEnergyHistoryStore(_root);
        var start = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.RecordSampleAsync(start, 600, TelemetrySource.HWiNFOBridge, cancellationToken);
        await store.RecordSampleAsync(start.AddMinutes(1), 600, TelemetrySource.HWiNFOBridge, cancellationToken);
        await store.RecordSampleAsync(start.AddMinutes(2), 300, TelemetrySource.Standalone, cancellationToken);
        await store.RecordSampleAsync(start.AddMinutes(3), 300, TelemetrySource.Standalone, cancellationToken);

        var day = await store.GetDayAsync(DateOnly.FromDateTime(start.LocalDateTime), cancellationToken);

        Assert.Equal(0.0175, day.KilowattHoursBySource[TelemetrySource.HWiNFOBridge], 6);
        Assert.Equal(0.005, day.KilowattHoursBySource[TelemetrySource.Standalone], 6);
        Assert.Contains("HWiNFO", day.SourceSummary);
        Assert.Contains("Standalone", day.SourceSummary);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        return ValueTask.CompletedTask;
    }
}
