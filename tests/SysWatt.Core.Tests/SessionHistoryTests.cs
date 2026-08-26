using SysWatt.Core.History;
using SysWatt.Core.Sensors;

namespace SysWatt.Core.Tests;

public sealed class SessionHistoryTests
{
    [Fact]
    public void EvictsOldestPointAtCapacity()
    {
        var history = new SessionHistory(2);
        history.Add(Snapshot(1));
        history.Add(Snapshot(2));
        history.Add(Snapshot(3));
        Assert.Equal([2d, 3d], history.Get(MetricKind.CpuUsage).Select(p => p.Value));
    }

    [Fact]
    public void GetWindowReturnsOnlyPointsWithinCutoff()
    {
        var history = new SessionHistory(10);
        history.Add(Snapshot(10));
        history.Add(Snapshot(20));
        history.Add(Snapshot(30));
        history.Add(Snapshot(40));

        var cutoff = DateTimeOffset.UnixEpoch.AddSeconds(25);
        var window = history.GetWindow(MetricKind.CpuUsage, cutoff);
        Assert.Equal([30d, 40d], window.Select(p => p.Value));
    }

    private static MetricSnapshot Snapshot(double value)
    {
        var at = DateTimeOffset.UnixEpoch.AddSeconds(value);
        return new(at, new Dictionary<MetricKind, MetricReading>
        {
            [MetricKind.CpuUsage] = new(MetricKind.CpuUsage, value, "%", at, false, null, null, null)
        });
    }
}
