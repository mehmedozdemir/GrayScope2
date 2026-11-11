namespace GrayScope.Core.Domain;

/// <summary>A Graylog server the user can query. <see cref="BaseUrl"/> includes the <c>/api</c> suffix.</summary>
public sealed class GraylogProfile
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";

    /// <summary>Protected (encrypted) API token. Never holds plaintext.</summary>
    public string TokenProtected { get; set; } = "";

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>True when the stored token is a placeholder that must be re-entered (e.g. after restore).</summary>
    public bool NeedsToken => string.IsNullOrEmpty(TokenProtected);

    public override string ToString() => Name;
}

/// <summary>A node of the query tree used only for organisation.</summary>
public sealed class QueryFolder
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? ParentId { get; set; }
    public int Position { get; set; }
}

public enum TimeRangeKind
{
    Relative,
    Absolute,
    Keyword,
}

public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>
/// A time window rule. Relative/Keyword are rules re-evaluated "from now" on every run;
/// Absolute is a fixed historical window.
/// </summary>
public sealed record TimeRangeSpec(
    TimeRangeKind Kind,
    int RangeSeconds = 300,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Keyword = null)
{
    public static TimeRangeSpec LastMinutes(int minutes) => new(TimeRangeKind.Relative, minutes * 60);
}

/// <summary>A Graylog stream reference stored with a query (id is server-specific, name is portable).</summary>
public sealed record StreamRef(string StreamId, string StreamName);

/// <summary>A saved, parametric query definition.</summary>
public sealed class SavedQuery
{
    /// <summary>Default maximum number of rows pulled per execution.</summary>
    public const int DefaultResultSize = 150;

    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long ProfileId { get; set; }
    public long? FolderId { get; set; }
    public int Position { get; set; }
    public string Template { get; set; } = "";
    public TimeRangeSpec TimeRange { get; set; } = TimeRangeSpec.LastMinutes(5);
    public List<string> Fields { get; set; } = [];
    public string? DefaultSortField { get; set; }
    public SortDirection DefaultSortDirection { get; set; } = SortDirection.Descending;

    /// <summary>Maximum rows to fetch. 0 means "all" (no limit sent to Graylog).</summary>
    public int ResultSize { get; set; } = DefaultResultSize;

    public List<StreamRef> Streams { get; set; } = [];
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public SavedQuery Clone() => new()
    {
        Id = Id,
        Name = Name,
        ProfileId = ProfileId,
        FolderId = FolderId,
        Position = Position,
        Template = Template,
        TimeRange = TimeRange,
        Fields = [.. Fields],
        DefaultSortField = DefaultSortField,
        DefaultSortDirection = DefaultSortDirection,
        ResultSize = ResultSize,
        Streams = [.. Streams],
        Description = Description,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };
}

/// <summary>One execution of a saved query, kept for the run-history panel.</summary>
public sealed class QueryRun
{
    public long Id { get; set; }
    public long QueryId { get; set; }
    public DateTime RunAt { get; set; }
    public int DurationMs { get; set; }
    public int ResultCount { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Parameter values used for the run (name → raw input).</summary>
    public Dictionary<string, string> Parameters { get; set; } = [];
}
