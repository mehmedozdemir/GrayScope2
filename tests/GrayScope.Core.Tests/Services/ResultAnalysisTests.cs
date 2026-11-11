using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Services;

namespace GrayScope.Core.Tests.Services;

public class ResultAnalysisTests
{
    private static ResultRecord Row(string id, string timestamp)
    {
        var message = Messages.Of(id, timestamp);
        var outcome = new TargetOutcome(new GraylogProfile(), new Dictionary<string, string>(), "", [message], null, TimeSpan.Zero, []);
        return new ResultRecord { Cells = [id], Message = message, Source = outcome, Timestamp = ResultTableBuilder.ParseTimestamp(timestamp) };
    }

    [Fact]
    public void Histogram_groups_points_into_nice_buckets()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var points = new DateTimeOffset?[] { t0, t0.AddSeconds(20), t0.AddMinutes(2), null, t0.AddMinutes(9).AddSeconds(59) };

        var buckets = ResultAnalyzer.Histogram(points, maxBuckets: 30);

        Assert.Equal(4, buckets.Sum(b => b.Count));
        Assert.True(buckets.Count <= 30);
        Assert.Equal(t0, buckets[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(30), buckets[0].End - buckets[0].Start);
    }

    [Fact]
    public void Histogram_of_empty_input_is_empty()
    {
        Assert.Empty(ResultAnalyzer.Histogram([]));
    }

    [Fact]
    public void TopValues_orders_by_frequency_and_labels_empty()
    {
        var top = ResultAnalyzer.TopValues(["a", "b", "a", "", null, "a"]);

        Assert.Equal("a", top[0].Value);
        Assert.Equal(3, top[0].Count);
        Assert.Equal(0.5, top[0].Ratio);
        Assert.Equal(ResultAnalyzer.EmptyLabel, top[1].Value);
    }

    [Fact]
    public void LiveTailBuffer_returns_only_unseen_rows_newest_first()
    {
        var buffer = new LiveTailBuffer(capacity: 3);
        buffer.Merge([Row("1", "2026-01-01T10:00:00Z"), Row("2", "2026-01-01T10:00:01Z")]);

        var fresh = buffer.Merge([Row("2", "2026-01-01T10:00:01Z"), Row("3", "2026-01-01T10:00:02Z"), Row("4", "2026-01-01T10:00:03Z")]);

        Assert.Equal(["4", "3"], fresh.OrderByDescending(r => r.Timestamp).Select(r => r.Key));
        Assert.Equal(["4", "3", "2"], buffer.Rows.Select(r => r.Key));
    }

    [Fact]
    public void LiveTailBuffer_does_not_resurrect_rows_dropped_by_capacity()
    {
        var buffer = new LiveTailBuffer(capacity: 1);
        buffer.Merge([Row("1", "2026-01-01T10:00:00Z")]);
        buffer.Merge([Row("2", "2026-01-01T10:00:01Z")]);

        var fresh = buffer.Merge([Row("1", "2026-01-01T10:00:00Z")]);

        Assert.Empty(fresh);
    }

    [Fact]
    public void CellValueComparer_sorts_numbers_numerically_and_empty_last()
    {
        var values = new List<string?> { "10", "", "9", "100" };
        values.Sort(CellValueComparer.Instance);
        Assert.Equal(["9", "10", "100", ""], values);
    }

    [Fact]
    public void Build_infers_columns_when_query_has_no_fields()
    {
        var message = new MessageRecord
        {
            Fields = new Dictionary<string, string> { ["_id"] = "1", ["zeta"] = "z", ["message"] = "m", ["timestamp"] = "t", ["gl2_source_index"] = "i" },
        };
        var outcome = new TargetOutcome(new GraylogProfile { Id = 1 }, new Dictionary<string, string>(), "", [message], null, TimeSpan.Zero, []);

        var table = ResultTableBuilder.Build(new SavedQuery(), [outcome], TimeSpan.Zero);

        Assert.Equal(["timestamp", "message", "zeta"], table.Columns);
    }
}
