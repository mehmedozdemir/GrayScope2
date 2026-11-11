using System.Text.Json;
using Dapper;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;

namespace GrayScope.Infrastructure.Persistence;

public sealed class ProfileRepository(Database db) : IProfileRepository
{
    private const string Columns = "Id, Name, BaseUrl, TokenProtected, IsDefault, IsActive, CreatedAt, UpdatedAt";

    public async Task<IReadOnlyList<GraylogProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<Row>($"SELECT {Columns} FROM GraylogProfile ORDER BY IsDefault DESC, Name").ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task<GraylogProfile?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        var row = await c.QuerySingleOrDefaultAsync<Row>($"SELECT {Columns} FROM GraylogProfile WHERE Id = @id", new { id }).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<GraylogProfile> SaveAsync(GraylogProfile profile, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        if (profile.IsDefault)
        {
            await c.ExecuteAsync("UPDATE GraylogProfile SET IsDefault = 0 WHERE Id <> @Id", new { profile.Id }, tx).ConfigureAwait(false);
        }
        var args = new
        {
            profile.Id,
            profile.Name,
            profile.BaseUrl,
            profile.TokenProtected,
            IsDefault = profile.IsDefault ? 1 : 0,
            IsActive = profile.IsActive ? 1 : 0,
            Now = Database.ToDb(now),
        };
        if (profile.Id == 0)
        {
            profile.Id = await c.ExecuteScalarAsync<long>(
                """
                INSERT INTO GraylogProfile (Name, BaseUrl, TokenProtected, IsDefault, IsActive, CreatedAt, UpdatedAt)
                VALUES (@Name, @BaseUrl, @TokenProtected, @IsDefault, @IsActive, @Now, @Now);
                SELECT last_insert_rowid();
                """, args, tx).ConfigureAwait(false);
            profile.CreatedAt = now;
        }
        else
        {
            await c.ExecuteAsync(
                """
                UPDATE GraylogProfile SET Name = @Name, BaseUrl = @BaseUrl, TokenProtected = @TokenProtected,
                       IsDefault = @IsDefault, IsActive = @IsActive, UpdatedAt = @Now
                WHERE Id = @Id
                """, args, tx).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        profile.UpdatedAt = now;
        return profile;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        await c.ExecuteAsync("DELETE FROM GraylogProfile WHERE Id = @id", new { id }).ConfigureAwait(false);
    }

    private static GraylogProfile Map(Row r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        BaseUrl = r.BaseUrl,
        TokenProtected = r.TokenProtected,
        IsDefault = r.IsDefault != 0,
        IsActive = r.IsActive != 0,
        CreatedAt = Database.FromDb(r.CreatedAt),
        UpdatedAt = Database.FromDb(r.UpdatedAt),
    };

    private sealed record Row(long Id, string Name, string BaseUrl, string TokenProtected, long IsDefault, long IsActive, string CreatedAt, string UpdatedAt);
}

public sealed class FolderRepository(Database db) : IFolderRepository
{
    public async Task<IReadOnlyList<QueryFolder>> GetAllAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<(long Id, string Name, long? ParentId, long Position)>(
            "SELECT Id, Name, ParentId, Position FROM QueryFolder ORDER BY Position, Name").ConfigureAwait(false);
        return rows.Select(r => new QueryFolder { Id = r.Id, Name = r.Name, ParentId = r.ParentId, Position = (int)r.Position }).ToList();
    }

    public async Task<QueryFolder> SaveAsync(QueryFolder folder, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        if (folder.Id == 0)
        {
            folder.Id = await c.ExecuteScalarAsync<long>(
                "INSERT INTO QueryFolder (Name, ParentId, Position) VALUES (@Name, @ParentId, @Position); SELECT last_insert_rowid();",
                folder).ConfigureAwait(false);
        }
        else
        {
            await c.ExecuteAsync("UPDATE QueryFolder SET Name = @Name, ParentId = @ParentId, Position = @Position WHERE Id = @Id", folder).ConfigureAwait(false);
        }
        return folder;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        await c.ExecuteAsync("DELETE FROM QueryFolder WHERE Id = @id", new { id }).ConfigureAwait(false);
    }
}

public sealed class QueryRepository(Database db) : IQueryRepository
{
    private const string Select =
        """
        SELECT Id, Name, ProfileId, FolderId, Position, Template, Description, TimeRangeKind, RangeSeconds,
               RangeFrom, RangeTo, RangeKeyword, FieldsJson, DefaultSortField, DefaultSortDirection, ResultSize,
               CreatedAt, UpdatedAt
        FROM SavedQuery
        """;

    public async Task<IReadOnlyList<SavedQuery>> GetAllAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<Row>(Select + " ORDER BY Position, Name").ConfigureAwait(false);
        var streams = (await c.QueryAsync<(long QueryId, string StreamId, string StreamName)>(
            "SELECT QueryId, StreamId, StreamName FROM QueryStream ORDER BY Id").ConfigureAwait(false))
            .ToLookup(s => s.QueryId);
        return rows.Select(r => Map(r, streams[r.Id].Select(s => new StreamRef(s.StreamId, s.StreamName)))).ToList();
    }

    public async Task<SavedQuery?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        var row = await c.QuerySingleOrDefaultAsync<Row>(Select + " WHERE Id = @id", new { id }).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }
        var streams = await c.QueryAsync<(string StreamId, string StreamName)>(
            "SELECT StreamId, StreamName FROM QueryStream WHERE QueryId = @id ORDER BY Id", new { id }).ConfigureAwait(false);
        return Map(row, streams.Select(s => new StreamRef(s.StreamId, s.StreamName)));
    }

    public async Task<SavedQuery> SaveAsync(SavedQuery query, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var args = new
        {
            query.Id,
            query.Name,
            query.ProfileId,
            query.FolderId,
            query.Position,
            query.Template,
            query.Description,
            TimeRangeKind = query.TimeRange.Kind.ToString(),
            query.TimeRange.RangeSeconds,
            RangeFrom = Database.ToDb(query.TimeRange.From),
            RangeTo = Database.ToDb(query.TimeRange.To),
            RangeKeyword = query.TimeRange.Keyword,
            FieldsJson = JsonSerializer.Serialize(query.Fields),
            query.DefaultSortField,
            DefaultSortDirection = query.DefaultSortDirection.ToString(),
            query.ResultSize,
            Now = Database.ToDb(now),
        };

        if (query.Id == 0)
        {
            query.Id = await c.ExecuteScalarAsync<long>(
                """
                INSERT INTO SavedQuery (Name, ProfileId, FolderId, Position, Template, Description, TimeRangeKind, RangeSeconds,
                    RangeFrom, RangeTo, RangeKeyword, FieldsJson, DefaultSortField, DefaultSortDirection, ResultSize, CreatedAt, UpdatedAt)
                VALUES (@Name, @ProfileId, @FolderId, @Position, @Template, @Description, @TimeRangeKind, @RangeSeconds,
                    @RangeFrom, @RangeTo, @RangeKeyword, @FieldsJson, @DefaultSortField, @DefaultSortDirection, @ResultSize, @Now, @Now);
                SELECT last_insert_rowid();
                """, args, tx).ConfigureAwait(false);
            query.CreatedAt = now;
        }
        else
        {
            await c.ExecuteAsync(
                """
                UPDATE SavedQuery SET Name = @Name, ProfileId = @ProfileId, FolderId = @FolderId, Position = @Position,
                    Template = @Template, Description = @Description, TimeRangeKind = @TimeRangeKind, RangeSeconds = @RangeSeconds,
                    RangeFrom = @RangeFrom, RangeTo = @RangeTo, RangeKeyword = @RangeKeyword, FieldsJson = @FieldsJson,
                    DefaultSortField = @DefaultSortField, DefaultSortDirection = @DefaultSortDirection, ResultSize = @ResultSize,
                    UpdatedAt = @Now
                WHERE Id = @Id
                """, args, tx).ConfigureAwait(false);
            await c.ExecuteAsync("DELETE FROM QueryStream WHERE QueryId = @Id", new { query.Id }, tx).ConfigureAwait(false);
        }

        foreach (var stream in query.Streams)
        {
            await c.ExecuteAsync(
                "INSERT INTO QueryStream (QueryId, StreamId, StreamName) VALUES (@QueryId, @StreamId, @StreamName)",
                new { QueryId = query.Id, stream.StreamId, stream.StreamName }, tx).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        query.UpdatedAt = now;
        return query;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        await c.ExecuteAsync("DELETE FROM SavedQuery WHERE Id = @id", new { id }).ConfigureAwait(false);
    }

    public async Task<int> CountByProfileAsync(long profileId, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SavedQuery WHERE ProfileId = @profileId", new { profileId }).ConfigureAwait(false);
    }

    public async Task MoveAsync(long queryId, long? folderId, int position, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        await c.ExecuteAsync("UPDATE SavedQuery SET FolderId = @folderId, Position = @position WHERE Id = @queryId",
            new { queryId, folderId, position }).ConfigureAwait(false);
    }

    private static SavedQuery Map(Row r, IEnumerable<StreamRef> streams) => new()
    {
        Id = r.Id,
        Name = r.Name,
        ProfileId = r.ProfileId,
        FolderId = r.FolderId,
        Position = (int)r.Position,
        Template = r.Template,
        Description = r.Description,
        TimeRange = new TimeRangeSpec(
            Enum.TryParse<TimeRangeKind>(r.TimeRangeKind, out var kind) ? kind : TimeRangeKind.Relative,
            (int)r.RangeSeconds,
            Database.FromDbOffset(r.RangeFrom),
            Database.FromDbOffset(r.RangeTo),
            r.RangeKeyword),
        Fields = JsonSerializer.Deserialize<List<string>>(r.FieldsJson) ?? [],
        DefaultSortField = r.DefaultSortField,
        DefaultSortDirection = Enum.TryParse<SortDirection>(r.DefaultSortDirection, out var dir) ? dir : SortDirection.Descending,
        ResultSize = (int)r.ResultSize,
        Streams = streams.ToList(),
        CreatedAt = Database.FromDb(r.CreatedAt),
        UpdatedAt = Database.FromDb(r.UpdatedAt),
    };

    private sealed record Row(
        long Id, string Name, long ProfileId, long? FolderId, long Position, string Template, string? Description,
        string TimeRangeKind, long RangeSeconds, string? RangeFrom, string? RangeTo, string? RangeKeyword, string FieldsJson,
        string? DefaultSortField, string DefaultSortDirection, long ResultSize, string CreatedAt, string UpdatedAt);
}

public sealed class RunHistoryRepository(Database db) : IRunHistoryRepository
{
    /// <summary>History rows kept per query; older ones are pruned on insert.</summary>
    public const int RetainPerQuery = 100;

    private const string Columns = "Id, QueryId, RunAt, DurationMs, ResultCount, IsSuccess, ErrorMessage, ParametersJson";

    public async Task AddAsync(QueryRun run, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        run.Id = await c.ExecuteScalarAsync<long>(
            """
            INSERT INTO QueryRun (QueryId, RunAt, DurationMs, ResultCount, IsSuccess, ErrorMessage, ParametersJson)
            VALUES (@QueryId, @RunAt, @DurationMs, @ResultCount, @IsSuccess, @ErrorMessage, @ParametersJson);
            SELECT last_insert_rowid();
            """,
            new
            {
                run.QueryId,
                RunAt = Database.ToDb(run.RunAt),
                run.DurationMs,
                run.ResultCount,
                IsSuccess = run.IsSuccess ? 1 : 0,
                run.ErrorMessage,
                ParametersJson = JsonSerializer.Serialize(run.Parameters),
            }, tx).ConfigureAwait(false);
        await c.ExecuteAsync(
            """
            DELETE FROM QueryRun WHERE QueryId = @QueryId AND Id NOT IN
                (SELECT Id FROM QueryRun WHERE QueryId = @QueryId ORDER BY RunAt DESC LIMIT @Keep)
            """, new { run.QueryId, Keep = RetainPerQuery }, tx).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QueryRun>> GetRecentAsync(long queryId, int limit, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<Row>($"SELECT {Columns} FROM QueryRun WHERE QueryId = @queryId ORDER BY RunAt DESC LIMIT @limit",
            new { queryId, limit }).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyDictionary<long, QueryRun>> GetLatestPerQueryAsync(CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<Row>(
            $"""
            SELECT {Columns} FROM QueryRun r
            WHERE r.Id = (SELECT r2.Id FROM QueryRun r2 WHERE r2.QueryId = r.QueryId ORDER BY r2.RunAt DESC LIMIT 1)
            """).ConfigureAwait(false);
        return rows.Select(Map).ToDictionary(r => r.QueryId);
    }

    private static QueryRun Map(Row r) => new()
    {
        Id = r.Id,
        QueryId = r.QueryId,
        RunAt = Database.FromDb(r.RunAt),
        DurationMs = (int)r.DurationMs,
        ResultCount = (int)r.ResultCount,
        IsSuccess = r.IsSuccess != 0,
        ErrorMessage = r.ErrorMessage,
        Parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(r.ParametersJson) ?? [],
    };

    private sealed record Row(long Id, long QueryId, string RunAt, long DurationMs, long ResultCount, long IsSuccess, string? ErrorMessage, string ParametersJson);
}

public sealed class SettingsStore(Database db) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        return await c.ExecuteScalarAsync<string?>("SELECT Value FROM Setting WHERE Key = @key", new { key }).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct).ConfigureAwait(false);
        await c.ExecuteAsync("INSERT INTO Setting (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value",
            new { key, value }).ConfigureAwait(false);
    }
}
