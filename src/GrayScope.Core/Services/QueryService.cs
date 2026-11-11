using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;

namespace GrayScope.Core.Services;

/// <summary>Saved query and folder management.</summary>
public sealed class QueryService(IQueryRepository queries, IFolderRepository folders, IRunHistoryRepository history)
{
    public const int MinResultSize = 0;
    public const int MaxResultSize = 100_000;

    public Task<IReadOnlyList<SavedQuery>> GetAllAsync(CancellationToken ct = default) => queries.GetAllAsync(ct);
    public Task<IReadOnlyList<QueryFolder>> GetFoldersAsync(CancellationToken ct = default) => folders.GetAllAsync(ct);
    public Task<SavedQuery?> GetAsync(long id, CancellationToken ct = default) => queries.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<QueryRun>> GetHistoryAsync(long queryId, int limit = 30, CancellationToken ct = default) =>
        history.GetRecentAsync(queryId, limit, ct);

    public Task<IReadOnlyDictionary<long, QueryRun>> GetLatestRunsAsync(CancellationToken ct = default) =>
        history.GetLatestPerQueryAsync(ct);

    public async Task<SavedQuery> SaveAsync(SavedQuery query, CancellationToken ct = default)
    {
        Validate(query);
        var all = await queries.GetAllAsync(ct).ConfigureAwait(false);
        if (all.Any(q => q.Id != query.Id && string.Equals(q.Name, query.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException($"'{query.Name}' adında bir sorgu zaten var.");
        }
        return await queries.SaveAsync(query, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(long id, CancellationToken ct = default) => queries.DeleteAsync(id, ct);

    /// <summary>Copies a query (streams and folder included) under a unique "(kopya)" name.</summary>
    public async Task<SavedQuery> DuplicateAsync(long id, CancellationToken ct = default)
    {
        var source = await queries.GetByIdAsync(id, ct).ConfigureAwait(false)
            ?? throw new ValidationException("Kopyalanacak sorgu bulunamadı.");
        var names = (await queries.GetAllAsync(ct).ConfigureAwait(false)).Select(q => q.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copy = source.Clone();
        copy.Id = 0;
        copy.Name = UniqueName(source.Name + " (kopya)", names);
        return await queries.SaveAsync(copy, ct).ConfigureAwait(false);
    }

    public Task MoveAsync(long queryId, long? folderId, int position, CancellationToken ct = default) =>
        queries.MoveAsync(queryId, folderId, position, ct);

    public async Task<QueryFolder> SaveFolderAsync(QueryFolder folder, CancellationToken ct = default)
    {
        folder.Name = folder.Name.Trim();
        if (folder.Name.Length == 0)
        {
            throw new ValidationException("Klasör adı boş olamaz.");
        }
        if (folder.ParentId is { } parentId && await IsDescendantAsync(parentId, folder.Id, ct).ConfigureAwait(false))
        {
            throw new ValidationException("Bir klasör kendi alt klasörüne taşınamaz.");
        }
        return await folders.SaveAsync(folder, ct).ConfigureAwait(false);
    }

    public Task DeleteFolderAsync(long id, CancellationToken ct = default) => folders.DeleteAsync(id, ct);

    internal static string UniqueName(string baseName, IReadOnlySet<string> existing)
    {
        if (!existing.Contains(baseName))
        {
            return baseName;
        }
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    internal static void Validate(SavedQuery query)
    {
        query.Name = query.Name.Trim();
        if (query.Name.Length == 0)
        {
            throw new ValidationException("Sorgu adı boş olamaz.");
        }
        if (string.IsNullOrWhiteSpace(query.Template))
        {
            throw new ValidationException("Sorgu metni boş olamaz.");
        }
        if (query.ProfileId <= 0)
        {
            throw new ValidationException("Bir Graylog profili seçin.");
        }
        if (query.ResultSize is < MinResultSize or > MaxResultSize)
        {
            throw new ValidationException($"Sonuç limiti {MinResultSize}–{MaxResultSize} arasında olmalı (0 = tümü).");
        }
        TimeRangeValidator.Validate(query.TimeRange);
        query.Fields = query.Fields.Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private async Task<bool> IsDescendantAsync(long candidateParentId, long folderId, CancellationToken ct)
    {
        if (folderId == 0)
        {
            return false;
        }
        var byId = (await folders.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(f => f.Id);
        long? current = candidateParentId;
        while (current is { } id)
        {
            if (id == folderId)
            {
                return true;
            }
            current = byId.TryGetValue(id, out var f) ? f.ParentId : null;
        }
        return false;
    }
}

public static class TimeRangeValidator
{
    public static void Validate(TimeRangeSpec range)
    {
        switch (range.Kind)
        {
            case TimeRangeKind.Relative when range.RangeSeconds < 0:
                throw new ValidationException("Göreli zaman aralığı negatif olamaz.");
            case TimeRangeKind.Absolute when range.From is null || range.To is null:
                throw new ValidationException("Mutlak zaman aralığı için başlangıç ve bitiş girin.");
            case TimeRangeKind.Absolute when range.From > range.To:
                throw new ValidationException("Başlangıç zamanı bitişten sonra olamaz.");
            case TimeRangeKind.Keyword when string.IsNullOrWhiteSpace(range.Keyword):
                throw new ValidationException("Anahtar kelime zaman aralığı boş olamaz (örn. \"last 1 hour\").");
            default:
                return;
        }
    }
}
