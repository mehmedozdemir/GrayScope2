using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace GrayScope.Infrastructure.Persistence;

/// <summary>
/// Opens SQLite connections (foreign keys on) and applies forward-only migrations tracked in
/// <c>PRAGMA user_version</c>. Dates are stored as ISO-8601 UTC text.
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Ordered migrations; index + 1 is the schema version it produces. Never edit a shipped entry.</summary>
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE GraylogProfile (
            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
            Name           TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            BaseUrl        TEXT    NOT NULL,
            TokenProtected TEXT    NOT NULL DEFAULT '',
            IsDefault      INTEGER NOT NULL DEFAULT 0,
            IsActive       INTEGER NOT NULL DEFAULT 1,
            CreatedAt      TEXT    NOT NULL,
            UpdatedAt      TEXT    NOT NULL
        );
        CREATE TABLE QueryFolder (
            Id       INTEGER PRIMARY KEY AUTOINCREMENT,
            Name     TEXT    NOT NULL,
            ParentId INTEGER REFERENCES QueryFolder (Id) ON DELETE CASCADE,
            Position INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE SavedQuery (
            Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
            Name                 TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            ProfileId            INTEGER NOT NULL REFERENCES GraylogProfile (Id),
            FolderId             INTEGER REFERENCES QueryFolder (Id) ON DELETE SET NULL,
            Position             INTEGER NOT NULL DEFAULT 0,
            Template             TEXT    NOT NULL,
            Description          TEXT,
            TimeRangeKind        TEXT    NOT NULL,
            RangeSeconds         INTEGER NOT NULL DEFAULT 300,
            RangeFrom            TEXT,
            RangeTo              TEXT,
            RangeKeyword         TEXT,
            FieldsJson           TEXT    NOT NULL DEFAULT '[]',
            DefaultSortField     TEXT,
            DefaultSortDirection TEXT    NOT NULL DEFAULT 'Descending',
            ResultSize           INTEGER NOT NULL DEFAULT 150,
            CreatedAt            TEXT    NOT NULL,
            UpdatedAt            TEXT    NOT NULL
        );
        CREATE TABLE QueryStream (
            Id         INTEGER PRIMARY KEY AUTOINCREMENT,
            QueryId    INTEGER NOT NULL REFERENCES SavedQuery (Id) ON DELETE CASCADE,
            StreamId   TEXT    NOT NULL,
            StreamName TEXT    NOT NULL
        );
        CREATE INDEX IX_QueryStream_QueryId ON QueryStream (QueryId);
        CREATE TABLE QueryRun (
            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
            QueryId        INTEGER NOT NULL REFERENCES SavedQuery (Id) ON DELETE CASCADE,
            RunAt          TEXT    NOT NULL,
            DurationMs     INTEGER NOT NULL,
            ResultCount    INTEGER NOT NULL,
            IsSuccess      INTEGER NOT NULL,
            ErrorMessage   TEXT,
            ParametersJson TEXT    NOT NULL DEFAULT '{}'
        );
        CREATE INDEX IX_QueryRun_QueryId_RunAt ON QueryRun (QueryId, RunAt DESC);
        CREATE TABLE Setting (
            Key   TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """,
    ];

    public static int LatestVersion => Migrations.Length;

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    public void Migrate()
    {
        using var connection = Open();
        var version = connection.ExecuteScalar<long>("PRAGMA user_version;");
        for (var v = (int)version; v < Migrations.Length; v++)
        {
            using var tx = connection.BeginTransaction();
            connection.Execute(Migrations[v], transaction: tx);
            connection.Execute($"PRAGMA user_version = {v + 1};", transaction: tx);
            tx.Commit();
        }
    }

    public static string ToDb(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static string? ToDb(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static DateTime FromDb(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public static DateTimeOffset? FromDbOffset(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
