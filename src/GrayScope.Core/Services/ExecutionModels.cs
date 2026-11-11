using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;

namespace GrayScope.Core.Services;

/// <summary>Everything a run needs. Values here may differ from the saved query (ad-hoc edits are not persisted).</summary>
public sealed record ExecutionRequest
{
    public required SavedQuery Query { get; init; }

    /// <summary>Template text as currently shown in the editor.</summary>
    public required string Template { get; init; }

    /// <summary>Raw parameter inputs. In batch mode each value may be a comma-separated list.</summary>
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    public bool IsBatch { get; init; }

    /// <summary>Servers to run against. The first entry is the query's own profile unless the user changed it.</summary>
    public required IReadOnlyList<GraylogProfile> Profiles { get; init; }

    public required TimeRangeSpec TimeRange { get; init; }
    public required int Size { get; init; }

    /// <summary>False for live-tail ticks so periodic refreshes do not flood the run history.</summary>
    public bool RecordHistory { get; init; } = true;
}

/// <summary>Result of one (profile × parameter combination) search.</summary>
public sealed record TargetOutcome(
    GraylogProfile Profile,
    IReadOnlyDictionary<string, string> Parameters,
    string RenderedQuery,
    IReadOnlyList<MessageRecord> Messages,
    string? Error,
    TimeSpan Duration,
    IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Error is null;

    /// <summary>Short label such as <c>Ana Sunucu · NetworkId=34</c>.</summary>
    public string Label => Parameters.Count == 0
        ? Profile.Name
        : $"{Profile.Name} · {string.Join(", ", Parameters.Select(p => $"{p.Key}={p.Value}"))}";
}

/// <summary>One grid row. <see cref="Cells"/> aligns with <see cref="ResultTable.Columns"/>.</summary>
public sealed class ResultRecord
{
    public required string[] Cells { get; init; }
    public required MessageRecord Message { get; init; }
    public required TargetOutcome Source { get; init; }
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Stable identity used by live tail to detect new rows.</summary>
    public string Key => Message.MessageId ?? string.Join('\u001f', Cells);
}

public sealed class ResultTable
{
    /// <summary>Leading synthetic columns (source server / parameter values) shown only for batch runs.</summary>
    public required IReadOnlyList<string> ContextColumns { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }
    public required List<ResultRecord> Rows { get; init; }
    public required IReadOnlyList<TargetOutcome> Outcomes { get; init; }
    public required TimeSpan Duration { get; init; }

    public int FailedCount => Outcomes.Count(o => !o.IsSuccess);
    public bool IsPartialFailure => FailedCount > 0 && FailedCount < Outcomes.Count;
    public bool IsTotalFailure => Outcomes.Count > 0 && FailedCount == Outcomes.Count;
}
