using GrayScope.Core.Domain;

namespace GrayScope.Core.Abstractions;

public interface IProfileRepository
{
    Task<IReadOnlyList<GraylogProfile>> GetAllAsync(CancellationToken ct = default);
    Task<GraylogProfile?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<GraylogProfile> SaveAsync(GraylogProfile profile, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IFolderRepository
{
    Task<IReadOnlyList<QueryFolder>> GetAllAsync(CancellationToken ct = default);
    Task<QueryFolder> SaveAsync(QueryFolder folder, CancellationToken ct = default);

    /// <summary>Deletes the folder and its sub-folders; contained queries move to the root.</summary>
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IQueryRepository
{
    Task<IReadOnlyList<SavedQuery>> GetAllAsync(CancellationToken ct = default);
    Task<SavedQuery?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>Inserts or updates the query together with its stream list (one transaction).</summary>
    Task<SavedQuery> SaveAsync(SavedQuery query, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<int> CountByProfileAsync(long profileId, CancellationToken ct = default);
    Task MoveAsync(long queryId, long? folderId, int position, CancellationToken ct = default);
}

public interface IRunHistoryRepository
{
    Task AddAsync(QueryRun run, CancellationToken ct = default);
    Task<IReadOnlyList<QueryRun>> GetRecentAsync(long queryId, int limit, CancellationToken ct = default);

    /// <summary>Latest successful run per query, for tree badges.</summary>
    Task<IReadOnlyDictionary<long, QueryRun>> GetLatestPerQueryAsync(CancellationToken ct = default);
}

/// <summary>Small key/value store for user preferences (theme, window, tail interval...).</summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
}

/// <summary>Encrypts secrets at rest. Implementations must never log plaintext.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <exception cref="Errors.SecretUnavailableException">The value cannot be decrypted on this machine/user.</exception>
    string Unprotect(string protectedValue);
}
