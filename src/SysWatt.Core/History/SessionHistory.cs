using SysWatt.Core.Sensors;

namespace SysWatt.Core.History;

public sealed record HistoryPoint(DateTimeOffset Timestamp, double? Value);

public interface ISessionHistory
{
    void Add(MetricSnapshot snapshot);
    IReadOnlyList<HistoryPoint> Get(MetricKind metric);
    int Capacity { get; }
}

public sealed class SessionHistory : ISessionHistory
{
    private readonly object _gate = new();
    private readonly Dictionary<MetricKind, Queue<HistoryPoint>> _series;
    public int Capacity { get; }

    public SessionHistory(int capacity = 300)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _series = Enum.GetValues<MetricKind>().ToDictionary(k => k, _ => new Queue<HistoryPoint>(capacity));
    }

    public void Add(MetricSnapshot snapshot)
    {
        lock (_gate)
        {
            foreach (var (metric, queue) in _series)
            {
                queue.Enqueue(new HistoryPoint(snapshot.Timestamp, snapshot.Value(metric)));
                while (queue.Count > Capacity) queue.Dequeue();
            }
        }
    }

    public IReadOnlyList<HistoryPoint> Get(MetricKind metric)
    {
        lock (_gate) return _series[metric].ToArray();
    }
}
