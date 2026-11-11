using GrayScope.Core.Domain;

namespace GrayScope.Core.Abstractions;

public sealed record GraylogSystemInfo(string? Version, string? ClusterId, string? Hostname);

public sealed record StreamInfo(string Id, string Title);

/// <summary>Graylog API flavour detected for a server.</summary>
public enum GraylogApiFlavor
{
    Unknown,

    /// <summary>Graylog ≥ 5.x: POST /search/messages (Search Scripting API).</summary>
    Scripting,

    /// <summary>Graylog 2.x–4.x: GET /search/universal/*.</summary>
    Universal,
}

public sealed record SearchRequest(
    string Query,
    IReadOnlyList<string> StreamIds,
    TimeRangeSpec TimeRange,
    IReadOnlyList<string> Fields,
    int Size);

/// <summary>One message returned by a search. <see cref="Fields"/> holds every field the server returned.</summary>
public sealed class MessageRecord
{
    public required IReadOnlyDictionary<string, string> Fields { get; init; }

    /// <summary>Graylog message id (<c>_id</c>), when available.</summary>
    public string? MessageId { get; init; }

    /// <summary>Elasticsearch/OpenSearch index holding the message, when known.</summary>
    public string? Index { get; init; }

    /// <summary>True when <see cref="Fields"/> is already complete (2.x path); false when only the requested columns came back.</summary>
    public bool HasAllFields { get; init; }

    public string Get(string field) => Fields.TryGetValue(field, out var v) ? v : "";
}

public sealed record SearchResponse(IReadOnlyList<MessageRecord> Messages, GraylogApiFlavor Flavor, long? TotalResults = null);

/// <summary>Thin Graylog REST adapter. Holds no business rules.</summary>
public interface IGraylogClient
{
    Task<GraylogSystemInfo> GetSystemInfoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StreamInfo>> GetStreamsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetFieldNamesAsync(CancellationToken ct = default);
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct = default);

    /// <summary>Best-effort retrieval of every field of one message. Returns null when the server cannot provide it.</summary>
    Task<IReadOnlyDictionary<string, string>?> GetMessageAsync(string messageId, string? index, CancellationToken ct = default);
}

public interface IGraylogClientFactory
{
    /// <summary>Creates a client for <paramref name="profile"/>; the token is decrypted only in memory.</summary>
    IGraylogClient Create(GraylogProfile profile);

    /// <summary>Creates a client from raw values (used by "test connection" before saving).</summary>
    IGraylogClient Create(string baseUrl, string plaintextToken);
}
