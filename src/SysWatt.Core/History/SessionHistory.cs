using SysWatt.Core.Sensors;

namespace SysWatt.Core.History;

public sealed record HistoryPoint(DateTimeOffset Timestamp, double? Value);

public interface ISessionHistory
{
    void Add(MetricSnapshot snapshot);
    IReadOnlyList<HistoryPoint> Get(MetricKind metric);
    IReadOnlyList<HistoryPoint> GetWindow(MetricKind metric, DateTimeOffset cutoff);
    int Capacity { get; }
}

public sealed class SessionHistory : ISessionHistory
{
    private readonly object _gate = new();
    private readonly Dictionary<MetricKind, RingBuffer> _series;
    public int Capacity { get; }

    public SessionHistory(int capacity = 300)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _series = Enum.GetValues<MetricKind>().ToDictionary(k => k, _ => new RingBuffer(capacity));
    }

    public void Add(MetricSnapshot snapshot)
    {
        lock (_gate)
        {
            foreach (var (metric, buffer) in _series)
            {
                buffer.Add(new HistoryPoint(snapshot.Timestamp, snapshot.Value(metric)));
            }
        }
    }

    public IReadOnlyList<HistoryPoint> Get(MetricKind metric)
    {
        lock (_gate) return _series[metric].ToArray();
    }

    public IReadOnlyList<HistoryPoint> GetWindow(MetricKind metric, DateTimeOffset cutoff)
    {
        lock (_gate) return _series[metric].ToArraySince(cutoff);
    }

    private sealed class RingBuffer
    {
        private readonly HistoryPoint[] _items;
        private int _start;
        private int _count;

        public RingBuffer(int capacity)
        {
            _items = new HistoryPoint[capacity];
        }

        public void Add(HistoryPoint item)
        {
            if (_count < _items.Length)
            {
                _items[(_start + _count) % _items.Length] = item;
                _count++;
            }
            else
            {
                _items[_start] = item;
                _start = (_start + 1) % _items.Length;
            }
        }

        public HistoryPoint[] ToArray()
        {
            var result = new HistoryPoint[_count];
            for (var i = 0; i < _count; i++)
            {
                result[i] = _items[(_start + i) % _items.Length];
            }
            return result;
        }

        public HistoryPoint[] ToArraySince(DateTimeOffset cutoff)
        {
            var matchCount = 0;
            for (var i = _count - 1; i >= 0; i--)
            {
                var point = _items[(_start + i) % _items.Length];
                if (point.Timestamp < cutoff) break;
                matchCount++;
            }

            var result = new HistoryPoint[matchCount];
            var firstMatchIndex = _count - matchCount;
            for (var i = 0; i < matchCount; i++)
            {
                result[i] = _items[(_start + firstMatchIndex + i) % _items.Length];
            }
            return result;
        }
    }
}
