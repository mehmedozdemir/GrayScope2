using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;

namespace GrayScope.Core.Tests;

internal sealed class FakeGraylogClient : IGraylogClient
{
    public List<StreamInfo> Streams { get; } = [];
    public Func<SearchRequest, SearchResponse> OnSearch { get; set; } = _ => new SearchResponse([], GraylogApiFlavor.Scripting);
    public List<SearchRequest> Searches { get; } = [];

    public Task<GraylogSystemInfo> GetSystemInfoAsync(CancellationToken ct = default) => Task.FromResult(new GraylogSystemInfo("5.1.2", "c1", "h"));
    public Task<IReadOnlyList<StreamInfo>> GetStreamsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StreamInfo>>(Streams);
    public Task<IReadOnlyList<string>> GetFieldNamesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["source", "message"]);

    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        lock (Searches)
        {
            Searches.Add(request);
        }
        return Task.FromResult(OnSearch(request));
    }

    public Task<IReadOnlyDictionary<string, string>?> GetMessageAsync(string messageId, string? index, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>?>(new Dictionary<string, string> { ["_id"] = messageId, ["message"] = "full" });
}

internal sealed class FakeClientFactory : IGraylogClientFactory
{
    public Dictionary<long, FakeGraylogClient> Clients { get; } = [];

    public IGraylogClient Create(GraylogProfile profile) => Clients.TryGetValue(profile.Id, out var c) ? c : throw new GraylogConnectionException("no fake");
    public IGraylogClient Create(string baseUrl, string plaintextToken) => Clients.Values.First();
}

internal sealed class NullHistory : IRunHistoryRepository
{
    public List<QueryRun> Runs { get; } = [];
    public Task AddAsync(QueryRun run, CancellationToken ct = default)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<QueryRun>> GetRecentAsync(long queryId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<QueryRun>>(Runs);
    public Task<IReadOnlyDictionary<long, QueryRun>> GetLatestPerQueryAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<long, QueryRun>>(new Dictionary<long, QueryRun>());
}

internal sealed class InMemoryProfiles : IProfileRepository
{
    public List<GraylogProfile> Items { get; } = [];
    public Task<IReadOnlyList<GraylogProfile>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GraylogProfile>>([.. Items]);
    public Task<GraylogProfile?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
    public Task<GraylogProfile> SaveAsync(GraylogProfile profile, CancellationToken ct = default)
    {
        if (profile.Id == 0)
        {
            profile.Id = Items.Count + 1;
            Items.Add(profile);
        }
        return Task.FromResult(profile);
    }
    public Task DeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(Items.RemoveAll(p => p.Id == id));
}

internal sealed class InMemoryFolders : IFolderRepository
{
    public List<QueryFolder> Items { get; } = [];
    public Task<IReadOnlyList<QueryFolder>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<QueryFolder>>([.. Items]);
    public Task<QueryFolder> SaveAsync(QueryFolder folder, CancellationToken ct = default)
    {
        if (folder.Id == 0)
        {
            folder.Id = Items.Count + 1;
            Items.Add(folder);
        }
        return Task.FromResult(folder);
    }
    public Task DeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(Items.RemoveAll(f => f.Id == id));
}

internal sealed class InMemoryQueries : IQueryRepository
{
    public List<SavedQuery> Items { get; } = [];
    public Task<IReadOnlyList<SavedQuery>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SavedQuery>>([.. Items]);
    public Task<SavedQuery?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(q => q.Id == id)?.Clone());
    public Task<SavedQuery> SaveAsync(SavedQuery query, CancellationToken ct = default)
    {
        if (query.Id == 0)
        {
            query.Id = Items.Count + 1;
            Items.Add(query);
        }
        return Task.FromResult(query);
    }
    public Task DeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(Items.RemoveAll(q => q.Id == id));
    public Task<int> CountByProfileAsync(long profileId, CancellationToken ct = default) => Task.FromResult(Items.Count(q => q.ProfileId == profileId));
    public Task MoveAsync(long queryId, long? folderId, int position, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Reversible, obviously-not-secure protector for tests.</summary>
internal sealed class PrefixProtector : ISecretProtector
{
    public string Protect(string plaintext) => "p:" + plaintext;
    public string Unprotect(string protectedValue) =>
        protectedValue.StartsWith("p:", StringComparison.Ordinal) ? protectedValue[2..] : throw new SecretUnavailableException("nope");
}

internal static class Messages
{
    public static MessageRecord Of(string id, string timestamp, params (string Key, string Value)[] fields)
    {
        var map = fields.ToDictionary(f => f.Key, f => f.Value);
        map["_id"] = id;
        map["timestamp"] = timestamp;
        return new MessageRecord { Fields = map, MessageId = id };
    }
}
