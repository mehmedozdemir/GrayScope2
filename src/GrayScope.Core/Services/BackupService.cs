using System.Text.Json;
using System.Text.Json.Serialization;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;

namespace GrayScope.Core.Services;

/// <summary>Decrypts tokens written by GrayScope 1 (Fernet). Returns null when it cannot.</summary>
public interface ILegacyTokenDecryptor
{
    string? TryDecrypt(string fernetToken);
}

public sealed record ImportSummary(int ProfilesAdded, int FoldersAdded, int QueriesAdded, int QueriesSkipped, IReadOnlyList<string> Warnings);

/// <summary>
/// JSON backup/restore. Version 3 is written; versions 1 and 2 (GrayScope 1) are read.
/// Matching is by name and existing items are never overwritten.
/// </summary>
public sealed class BackupService(
    IProfileRepository profiles,
    IFolderRepository folders,
    IQueryRepository queries,
    ISecretProtector protector,
    ILegacyTokenDecryptor legacyDecryptor)
{
    public const int CurrentVersion = 3;
    private const char FolderPathSeparator = '/';

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<string> ExportAsync(bool includeTokens, CancellationToken ct = default)
    {
        var allProfiles = await profiles.GetAllAsync(ct).ConfigureAwait(false);
        var allFolders = await folders.GetAllAsync(ct).ConfigureAwait(false);
        var allQueries = await queries.GetAllAsync(ct).ConfigureAwait(false);
        var profileNames = allProfiles.ToDictionary(p => p.Id, p => p.Name);
        var paths = FolderPaths(allFolders);

        var document = new BackupDocument
        {
            Version = CurrentVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            Profiles = allProfiles.Select(p => new BackupProfile
            {
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                TokenProtected = includeTokens && !p.NeedsToken ? p.TokenProtected : null,
                IsDefault = p.IsDefault,
                IsActive = p.IsActive,
            }).ToList(),
            Folders = allFolders.Select(f => new BackupFolder { Name = f.Name, Path = paths[f.Id] }).ToList(),
            Queries = allQueries.Select(q => ToBackup(q, profileNames, paths)).ToList(),
        };
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public async Task<ImportSummary> ImportAsync(string json, CancellationToken ct = default)
    {
        BackupDocument document;
        try
        {
            document = JsonSerializer.Deserialize<BackupDocument>(json, JsonOptions)
                ?? throw new ValidationException("Yedek dosyası boş.");
        }
        catch (JsonException ex)
        {
            throw new ValidationException($"Yedek dosyası okunamadı: geçersiz JSON ({ex.Message}).");
        }
        return await ImportAsync(document, ct).ConfigureAwait(false);
    }

    public async Task<ImportSummary> ImportAsync(BackupDocument document, CancellationToken ct = default)
    {
        if (document.Version is < 1 or > CurrentVersion)
        {
            throw new ValidationException($"Desteklenmeyen yedek formatı (version={document.Version}).");
        }

        var warnings = new List<string>();
        var (profileIds, profilesAdded) = await ImportProfilesAsync(document, warnings, ct).ConfigureAwait(false);
        var (folderIds, foldersAdded) = await ImportFoldersAsync(document, ct).ConfigureAwait(false);

        var existing = (await queries.GetAllAsync(ct).ConfigureAwait(false)).Select(q => q.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0, skipped = 0;
        foreach (var q in document.Queries)
        {
            if (existing.Contains(q.Name))
            {
                skipped++;
                continue;
            }
            if (!profileIds.TryGetValue(q.ProfileName ?? "", out var profileId))
            {
                warnings.Add($"'{q.Name}': '{q.ProfileName}' profili bulunamadı, sorgu atlandı.");
                skipped++;
                continue;
            }
            var folderKey = q.FolderPath ?? q.Folder;
            var query = FromBackup(q, profileId, folderKey is not null && folderIds.TryGetValue(folderKey, out var fid) ? fid : null);
            await queries.SaveAsync(query, ct).ConfigureAwait(false);
            existing.Add(q.Name);
            added++;
        }
        return new ImportSummary(profilesAdded, foldersAdded, added, skipped, warnings);
    }

    private async Task<(Dictionary<string, long> Ids, int Added)> ImportProfilesAsync(BackupDocument document, List<string> warnings, CancellationToken ct)
    {
        var ids = (await profiles.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var p in document.Profiles)
        {
            if (ids.ContainsKey(p.Name))
            {
                continue;
            }
            var token = RecoverToken(p);
            if (token is null)
            {
                warnings.Add($"'{p.Name}' profilinin token'ı bu bilgisayarda çözülemedi. Profil oluşturuldu; Ayarlar'dan token'ı yeniden girin.");
            }
            var saved = await profiles.SaveAsync(new GraylogProfile
            {
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                TokenProtected = token ?? "",
                IsDefault = p.IsDefault && ids.Count == 0,
                IsActive = p.IsActive,
            }, ct).ConfigureAwait(false);
            ids[p.Name] = saved.Id;
            added++;
        }
        return (ids, added);
    }

    /// <summary>Returns a token protected for this machine, or null when the source cannot be decrypted.</summary>
    private string? RecoverToken(BackupProfile p)
    {
        if (!string.IsNullOrEmpty(p.TokenProtected))
        {
            try
            {
                protector.Unprotect(p.TokenProtected);
                return p.TokenProtected;
            }
            catch (SecretUnavailableException)
            {
                return null;
            }
        }
        if (!string.IsNullOrEmpty(p.TokenEncrypted) && p.TokenEncrypted != "PLACEHOLDER")
        {
            var plain = legacyDecryptor.TryDecrypt(p.TokenEncrypted);
            return plain is null ? null : protector.Protect(plain);
        }
        return null;
    }

    /// <summary>
    /// Creates missing folders. v3 identifies folders by full path; v1/v2 only by name + parent name,
    /// so for them the name itself is the lookup key (same behaviour as GrayScope 1).
    /// </summary>
    private async Task<(Dictionary<string, long> Ids, int Added)> ImportFoldersAsync(BackupDocument document, CancellationToken ct)
    {
        var current = await folders.GetAllAsync(ct).ConfigureAwait(false);
        var byPath = FolderPaths(current).ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
        foreach (var f in current)
        {
            byPath.TryAdd(f.Name, f.Id);
        }
        var added = 0;

        async Task<long> EnsureAsync(string path)
        {
            if (byPath.TryGetValue(path, out var id))
            {
                return id;
            }
            var cut = path.LastIndexOf(FolderPathSeparator);
            long? parentId = cut > 0 ? await EnsureAsync(path[..cut]).ConfigureAwait(false) : null;
            var saved = await folders.SaveAsync(new QueryFolder { Name = cut > 0 ? path[(cut + 1)..] : path, ParentId = parentId }, ct).ConfigureAwait(false);
            byPath[path] = saved.Id;
            added++;
            return saved.Id;
        }

        foreach (var f in document.Folders)
        {
            if (f.Path is not null)
            {
                await EnsureAsync(f.Path).ConfigureAwait(false);
                continue;
            }
            long? parentId = f.ParentName is null ? null : await EnsureAsync(f.ParentName).ConfigureAwait(false);
            if (!byPath.ContainsKey(f.Name))
            {
                var saved = await folders.SaveAsync(new QueryFolder { Name = f.Name, ParentId = parentId }, ct).ConfigureAwait(false);
                byPath[f.Name] = saved.Id;
                added++;
            }
        }
        return (byPath, added);
    }

    public static Dictionary<long, string> FolderPaths(IReadOnlyList<QueryFolder> all)
    {
        var byId = all.ToDictionary(f => f.Id);
        var result = new Dictionary<long, string>();
        foreach (var folder in all)
        {
            var parts = new List<string>();
            var guard = new HashSet<long>();
            for (QueryFolder? f = folder; f is not null && guard.Add(f.Id); f = f.ParentId is { } p && byId.TryGetValue(p, out var parent) ? parent : null)
            {
                parts.Insert(0, f.Name.Replace(FolderPathSeparator, '∕'));
            }
            result[folder.Id] = string.Join(FolderPathSeparator, parts);
        }
        return result;
    }

    private static BackupQuery ToBackup(SavedQuery q, Dictionary<long, string> profileNames, Dictionary<long, string> paths) => new()
    {
        Name = q.Name,
        FolderPath = q.FolderId is { } fid && paths.TryGetValue(fid, out var path) ? path : null,
        ProfileName = profileNames.GetValueOrDefault(q.ProfileId, ""),
        QueryTemplate = q.Template,
        Description = q.Description,
        TimeRangeType = q.TimeRange.Kind.ToString(),
        TimeRangeSeconds = q.TimeRange.Kind == TimeRangeKind.Relative ? q.TimeRange.RangeSeconds : null,
        TimeRangeFrom = q.TimeRange.From?.ToString("O"),
        TimeRangeTo = q.TimeRange.To?.ToString("O"),
        TimeRangeKeyword = q.TimeRange.Keyword,
        Fields = q.Fields,
        DefaultSortField = q.DefaultSortField,
        DefaultSortOrder = q.DefaultSortField is null ? null : q.DefaultSortDirection == SortDirection.Ascending ? "asc" : "desc",
        ResultSize = q.ResultSize,
        Streams = q.Streams.Select(s => new BackupStreamRef { StreamId = s.StreamId, StreamName = s.StreamName }).ToList(),
    };

    private static SavedQuery FromBackup(BackupQuery q, long profileId, long? folderId)
    {
        var kind = Enum.TryParse<TimeRangeKind>(q.TimeRangeType, ignoreCase: true, out var k) ? k : TimeRangeKind.Relative;
        return new SavedQuery
        {
            Name = q.Name,
            ProfileId = profileId,
            FolderId = folderId,
            Template = q.QueryTemplate ?? "",
            Description = q.Description,
            TimeRange = new TimeRangeSpec(kind, q.TimeRangeSeconds ?? 300, ParseDate(q.TimeRangeFrom), ParseDate(q.TimeRangeTo), q.TimeRangeKeyword),
            Fields = q.Fields ?? [],
            DefaultSortField = string.IsNullOrWhiteSpace(q.DefaultSortField) ? null : q.DefaultSortField,
            DefaultSortDirection = q.DefaultSortOrder == "asc" ? SortDirection.Ascending : SortDirection.Descending,
            ResultSize = q.ResultSize ?? SavedQuery.DefaultResultSize,
            Streams = (q.Streams ?? []).Select(s => new StreamRef(s.StreamId, s.StreamName)).ToList(),
        };
    }

    /// <summary>GrayScope 1 wrote naive local ISO timestamps; offsets are honoured when present.</summary>
    internal static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var d) ? d : null;
}

#pragma warning disable CA2227 // DTOs are (de)serialised; settable collections are intentional.
public sealed class BackupDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("exported_at")] public DateTimeOffset? ExportedAt { get; set; }
    [JsonPropertyName("profiles")] public List<BackupProfile> Profiles { get; set; } = [];
    [JsonPropertyName("folders")] public List<BackupFolder> Folders { get; set; } = [];
    [JsonPropertyName("queries")] public List<BackupQuery> Queries { get; set; } = [];
}

public sealed class BackupProfile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("base_url")] public string BaseUrl { get; set; } = "";

    /// <summary>GrayScope 1 Fernet ciphertext (read only).</summary>
    [JsonPropertyName("token_encrypted")] public string? TokenEncrypted { get; set; }

    /// <summary>GrayScope 2 protected token; only decryptable by the same Windows user.</summary>
    [JsonPropertyName("token_protected")] public string? TokenProtected { get; set; }

    [JsonPropertyName("is_default")] public bool IsDefault { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; } = true;
}

public sealed class BackupFolder
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("parent_name")] public string? ParentName { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
}

public sealed class BackupQuery
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("folder")] public string? Folder { get; set; }
    [JsonPropertyName("folder_path")] public string? FolderPath { get; set; }
    [JsonPropertyName("profile_name")] public string? ProfileName { get; set; }
    [JsonPropertyName("query_template")] public string? QueryTemplate { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("time_range_type")] public string? TimeRangeType { get; set; }
    [JsonPropertyName("time_range_seconds")] public int? TimeRangeSeconds { get; set; }
    [JsonPropertyName("time_range_from")] public string? TimeRangeFrom { get; set; }
    [JsonPropertyName("time_range_to")] public string? TimeRangeTo { get; set; }
    [JsonPropertyName("time_range_keyword")] public string? TimeRangeKeyword { get; set; }
    [JsonPropertyName("fields")] public List<string>? Fields { get; set; }
    [JsonPropertyName("default_sort_field")] public string? DefaultSortField { get; set; }
    [JsonPropertyName("default_sort_order")] public string? DefaultSortOrder { get; set; }
    [JsonPropertyName("result_size")] public int? ResultSize { get; set; }
    [JsonPropertyName("streams")] public List<BackupStreamRef>? Streams { get; set; }
}

public sealed class BackupStreamRef
{
    [JsonPropertyName("stream_id")] public string StreamId { get; set; } = "";
    [JsonPropertyName("stream_name")] public string StreamName { get; set; } = "";
}
#pragma warning restore CA2227
