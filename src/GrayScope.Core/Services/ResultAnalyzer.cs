namespace GrayScope.Core.Services;

public sealed record HistogramBucket(DateTimeOffset Start, DateTimeOffset End, int Count);

public sealed record ValueCount(string Value, int Count, double Ratio);

/// <summary>Client-side analysis over an already fetched result set (no extra Graylog calls).</summary>
public static class ResultAnalyzer
{
    /// <summary>Candidate bucket widths, smallest first; the first one producing ≤ maxBuckets wins.</summary>
    private static readonly TimeSpan[] NiceIntervals =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
    ];

    public static IReadOnlyList<HistogramBucket> Histogram(IEnumerable<DateTimeOffset?> timestamps, int maxBuckets = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBuckets, 1);
        var points = timestamps.Where(t => t.HasValue).Select(t => t!.Value.ToUniversalTime()).ToList();
        if (points.Count == 0)
        {
            return [];
        }

        var min = points.Min();
        var max = points.Max();
        var span = max - min;
        var interval = NiceIntervals.FirstOrDefault(i => span.Ticks / i.Ticks < maxBuckets, NiceIntervals[^1]);

        var start = new DateTimeOffset(min.Ticks - (min.Ticks % interval.Ticks), TimeSpan.Zero);
        var bucketCount = (int)((max - start).Ticks / interval.Ticks) + 1;
        var counts = new int[bucketCount];
        foreach (var p in points)
        {
            counts[(int)((p - start).Ticks / interval.Ticks)]++;
        }

        return counts.Select((c, i) => new HistogramBucket(start + (interval * i), start + (interval * (i + 1)), c)).ToList();
    }

    /// <summary>Most frequent values of one column. Empty values are reported as "(boş)".</summary>
    public static IReadOnlyList<ValueCount> TopValues(IEnumerable<string?> values, int top = 25)
    {
        var list = values.Select(v => string.IsNullOrEmpty(v) ? EmptyLabel : v).ToList();
        if (list.Count == 0)
        {
            return [];
        }
        return list.GroupBy(v => v, StringComparer.Ordinal)
            .Select(g => new ValueCount(g.Key, g.Count(), (double)g.Count() / list.Count))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Value, StringComparer.Ordinal)
            .Take(top)
            .ToList();
    }

    public const string EmptyLabel = "(boş)";

    /// <summary>Number of distinct values; useful to tell categorical fields apart from ids.</summary>
    public static int Cardinality(IEnumerable<string?> values) => values.Distinct(StringComparer.Ordinal).Count();
}

/// <summary>
/// Accumulates rows across live-tail ticks: keeps the newest rows first, detects unseen rows by
/// message id and caps memory use.
/// </summary>
public sealed class LiveTailBuffer(int capacity = LiveTailBuffer.DefaultCapacity)
{
    public const int DefaultCapacity = 5000;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<ResultRecord> _rows = [];

    public IReadOnlyList<ResultRecord> Rows => _rows;

    /// <summary>Adds unseen rows; returns only the newly added ones (for highlighting).</summary>
    public IReadOnlyList<ResultRecord> Merge(IEnumerable<ResultRecord> incoming)
    {
        var fresh = incoming.Where(r => _seen.Add(r.Key)).ToList();
        if (fresh.Count == 0)
        {
            return fresh;
        }
        _rows.InsertRange(0, fresh.OrderByDescending(r => r.Timestamp));
        if (_rows.Count > capacity)
        {
            // Dropped rows stay in _seen: they are still inside the search window and must not reappear as "new".
            _rows.RemoveRange(capacity, _rows.Count - capacity);
        }
        return fresh;
    }

    public void Clear()
    {
        _seen.Clear();
        _rows.Clear();
    }
}
