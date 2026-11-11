using System.Text.Json;
using Dapper;
using GrayScope.Core.Errors;
using GrayScope.Core.Services;
using Microsoft.Data.Sqlite;

namespace GrayScope.Infrastructure.Legacy;

/// <summary>
/// Reads a GrayScope 1 SQLite database (read-only) and converts it to a version-2 backup document,
/// so the regular <see cref="BackupService"/> import path (and its Fernet token recovery) is reused.
/// </summary>
public static class LegacyDatabaseReader
{
    public static async Task<BackupDocument> ReadAsync(string databasePath, CancellationToken ct = default)
    {
        if (!File.Exists(databasePath))
        {
            throw new ValidationException("GrayScope 1 veritabanı dosyası bulunamadı.");
        }

        var cs = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync(ct).ConfigureAwait(false);

        var tables = (await c.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!tables.Contains("GraylogProfile") || !tables.Contains("Query"))
        {
            throw new ValidationException("Seçilen dosya bir GrayScope 1 veritabanı değil.");
        }

        var profiles = (await c.QueryAsync<(long Id, string Name, string BaseUrl, string TokenEncrypted, long IsDefault, long IsActive)>(
            "SELECT Id, Name, BaseUrl, TokenEncrypted, IsDefault, IsActive FROM GraylogProfile").ConfigureAwait(false)).ToList();
        var folders = tables.Contains("QueryFolder")
            ? (await c.QueryAsync<(long Id, string Name, long? ParentId)>("SELECT Id, Name, ParentId FROM QueryFolder").ConfigureAwait(false)).ToList()
            : [];
        var queryColumns = (await c.QueryAsync<string>("SELECT name FROM pragma_table_info('Query')").ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folderSelect = queryColumns.Contains("FolderId") ? "FolderId" : "NULL AS FolderId";
        var queries = (await c.QueryAsync<LegacyQuery>(
            $"""
            SELECT Id, Name, GraylogProfileId, QueryTemplate, TimeRangeType, TimeRangeRangeSeconds, TimeRangeFrom, TimeRangeTo,
                   TimeRangeKeyword, FieldsJson, DefaultSortField, DefaultSortOrder, ResultSize, {folderSelect}
            FROM Query
            """).ConfigureAwait(false)).ToList();
        var streams = tables.Contains("QueryStream")
            ? (await c.QueryAsync<(long QueryId, string StreamId, string StreamName)>("SELECT QueryId, StreamId, StreamName FROM QueryStream").ConfigureAwait(false)).ToLookup(s => s.QueryId)
            : Enumerable.Empty<(long QueryId, string StreamId, string StreamName)>().ToLookup(s => s.QueryId);

        var profileNames = profiles.ToDictionary(p => p.Id, p => p.Name);
        var folderNames = folders.ToDictionary(f => f.Id, f => f.Name);

        return new BackupDocument
        {
            Version = 2,
            Profiles = profiles.Select(p => new BackupProfile
            {
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                TokenEncrypted = p.TokenEncrypted,
                IsDefault = p.IsDefault != 0,
                IsActive = p.IsActive != 0,
            }).ToList(),
            Folders = folders.Select(f => new BackupFolder
            {
                Name = f.Name,
                ParentName = f.ParentId is { } pid ? folderNames.GetValueOrDefault(pid) : null,
            }).ToList(),
            Queries = queries.Select(q => new BackupQuery
            {
                Name = q.Name,
                Folder = q.FolderId is { } fid ? folderNames.GetValueOrDefault(fid) : null,
                ProfileName = profileNames.GetValueOrDefault(q.GraylogProfileId, ""),
                QueryTemplate = q.QueryTemplate,
                TimeRangeType = q.TimeRangeType,
                TimeRangeSeconds = (int?)q.TimeRangeRangeSeconds,
                TimeRangeFrom = q.TimeRangeFrom,
                TimeRangeTo = q.TimeRangeTo,
                TimeRangeKeyword = q.TimeRangeKeyword,
                Fields = string.IsNullOrEmpty(q.FieldsJson) ? [] : JsonSerializer.Deserialize<List<string>>(q.FieldsJson) ?? [],
                DefaultSortField = q.DefaultSortField,
                DefaultSortOrder = q.DefaultSortOrder,
                ResultSize = (int)q.ResultSize,
                Streams = streams[q.Id].Select(s => new BackupStreamRef { StreamId = s.StreamId, StreamName = s.StreamName }).ToList(),
            }).ToList(),
        };
    }

    private sealed record LegacyQuery(
        long Id, string Name, long GraylogProfileId, string QueryTemplate, string TimeRangeType, long? TimeRangeRangeSeconds,
        string? TimeRangeFrom, string? TimeRangeTo, string? TimeRangeKeyword, string? FieldsJson, string? DefaultSortField,
        string? DefaultSortOrder, long ResultSize, long? FolderId);
}
