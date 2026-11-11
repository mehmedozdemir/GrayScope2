using System.Globalization;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;

namespace GrayScope.Core.Services;

/// <summary>Turns raw search outcomes into a flat, sortable grid model.</summary>
public static class ResultTableBuilder
{
    public const string ServerColumn = "Sunucu";
    public const string TimestampField = "timestamp";

    /// <summary>Fields Graylog adds for bookkeeping; hidden when the query has no explicit field list.</summary>
    private static readonly HashSet<string> InternalFields = new(StringComparer.Ordinal)
    {
        "_id", "gl2_source_index", "gl2_message_id", "gl2_source_input", "gl2_source_node",
        "gl2_accounted_message_size", "gl2_processing_timestamp", "gl2_receive_timestamp",
        "gl2_remote_ip", "gl2_remote_port", "gl2_processing_duration_ms", "streams",
    };

    private static readonly string[] PreferredFirst = [TimestampField, "source", "message"];

    public static ResultTable Build(SavedQuery query, IReadOnlyList<TargetOutcome> outcomes, TimeSpan duration)
    {
        var successful = outcomes.Where(o => o.IsSuccess).ToList();
        var contextColumns = ContextColumns(outcomes);
        var dataColumns = query.Fields.Count > 0 ? query.Fields.ToList() : InferColumns(successful);

        var rows = new List<ResultRecord>();
        foreach (var outcome in successful)
        {
            var context = ContextValues(outcome, contextColumns);
            foreach (var message in outcome.Messages)
            {
                var cells = new string[context.Length + dataColumns.Count];
                context.CopyTo(cells, 0);
                for (var i = 0; i < dataColumns.Count; i++)
                {
                    cells[context.Length + i] = message.Get(dataColumns[i]);
                }
                rows.Add(new ResultRecord
                {
                    Cells = cells,
                    Message = message,
                    Source = outcome,
                    Timestamp = ParseTimestamp(message.Get(TimestampField)),
                });
            }
        }

        ApplyDefaultSort(query, rows, [.. contextColumns, .. dataColumns], outcomes.Count > 1);
        return new ResultTable
        {
            ContextColumns = contextColumns,
            Columns = [.. contextColumns, .. dataColumns],
            Rows = rows,
            Outcomes = outcomes,
            Duration = duration,
        };
    }

    public static DateTimeOffset? ParseTimestamp(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ts) ? ts : null;

    private static List<string> ContextColumns(IReadOnlyList<TargetOutcome> outcomes)
    {
        var columns = new List<string>();
        if (outcomes.Select(o => o.Profile.Id).Distinct().Count() > 1)
        {
            columns.Add(ServerColumn);
        }
        var parameterNames = outcomes.SelectMany(o => o.Parameters.Keys).Distinct(StringComparer.Ordinal);
        foreach (var name in parameterNames)
        {
            var distinctValues = outcomes.Select(o => o.Parameters.GetValueOrDefault(name, "")).Distinct(StringComparer.Ordinal).Count();
            if (distinctValues > 1)
            {
                columns.Add(ParameterColumnName(name));
            }
        }
        return columns;
    }

    public static string ParameterColumnName(string parameter) => "{" + parameter + "}";

    private static string[] ContextValues(TargetOutcome outcome, List<string> contextColumns) =>
        contextColumns.Select(c => c == ServerColumn
            ? outcome.Profile.Name
            : outcome.Parameters.GetValueOrDefault(c.Trim('{', '}'), "")).ToArray();

    private static List<string> InferColumns(List<TargetOutcome> outcomes)
    {
        var all = outcomes.SelectMany(o => o.Messages).SelectMany(m => m.Fields.Keys)
            .Where(f => !InternalFields.Contains(f))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var ordered = PreferredFirst.Where(all.Contains).ToList();
        ordered.AddRange(all.Except(PreferredFirst).Order(StringComparer.OrdinalIgnoreCase));
        return ordered;
    }

    private static void ApplyDefaultSort(SavedQuery query, List<ResultRecord> rows, List<string> columns, bool isMerged)
    {
        var sortIndex = query.DefaultSortField is null ? -1 : columns.IndexOf(query.DefaultSortField);
        if (sortIndex >= 0)
        {
            var comparer = CellValueComparer.Instance;
            var sign = query.DefaultSortDirection == SortDirection.Ascending ? 1 : -1;
            rows.Sort((a, b) => sign * comparer.Compare(a.Cells[sortIndex], b.Cells[sortIndex]));
        }
        else if (isMerged)
        {
            // Results from several searches: interleave by time, newest first like Graylog itself.
            rows.Sort((a, b) => Nullable.Compare(b.Timestamp, a.Timestamp));
        }
    }
}

/// <summary>Compares grid cells numerically when both sides are numbers, otherwise culture-aware text.</summary>
public sealed class CellValueComparer : IComparer<string?>
{
    public static readonly CellValueComparer Instance = new();
    private static readonly CompareInfo Turkish = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public int Compare(string? x, string? y)
    {
        if (double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
            double.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
        {
            return a.CompareTo(b);
        }
        if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y))
        {
            // Empty cells always sink to the end of an ascending sort.
            return string.IsNullOrEmpty(x) == string.IsNullOrEmpty(y) ? 0 : string.IsNullOrEmpty(x) ? 1 : -1;
        }
        return Turkish.Compare(x, y, CompareOptions.IgnoreCase);
    }
}
