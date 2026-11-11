using System.Text;
using Dapper;
using GrayScope.Core.Errors;
using GrayScope.Infrastructure.Export;
using GrayScope.Infrastructure.Legacy;
using Microsoft.Data.Sqlite;

namespace GrayScope.Infrastructure.Tests.Export;

public class ExportAndLegacyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Csv_export_escapes_and_writes_bom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gs2-{Guid.NewGuid():N}.csv");
        try
        {
            await ResultExporter.WriteCsvAsync(path, ["a", "b"], [["x,y", "say \"hi\""], ["ş", ""]], Ct);

            var bytes = await File.ReadAllBytesAsync(path, Ct);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            var text = Encoding.UTF8.GetString(bytes[3..]).ReplaceLineEndings("\n");
            Assert.Equal("a,b\n\"x,y\",\"say \"\"hi\"\"\"\nş,\n", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Excel_export_creates_workbook()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gs2-{Guid.NewGuid():N}.xlsx");
        try
        {
            await ResultExporter.WriteExcelAsync(path, ["a"], [["1"], [new string('x', 40_000)]], Ct);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Legacy_database_is_converted_to_backup_document()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gs1-{Guid.NewGuid():N}.db");
        try
        {
            await using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await c.OpenAsync(Ct);
                await c.ExecuteAsync("""
                    CREATE TABLE GraylogProfile (Id INTEGER PRIMARY KEY, Name TEXT, BaseUrl TEXT, TokenEncrypted TEXT, IsDefault INTEGER, IsActive INTEGER, CreatedAt TEXT, UpdatedAt TEXT);
                    CREATE TABLE QueryFolder (Id INTEGER PRIMARY KEY, Name TEXT, ParentId INTEGER, Position INTEGER);
                    CREATE TABLE Query (Id INTEGER PRIMARY KEY, Name TEXT, GraylogProfileId INTEGER, QueryTemplate TEXT, TimeRangeType TEXT,
                        TimeRangeRangeSeconds INTEGER, TimeRangeFrom TEXT, TimeRangeTo TEXT, TimeRangeKeyword TEXT, FieldsJson TEXT,
                        DefaultSortField TEXT, DefaultSortOrder TEXT, ResultSize INTEGER, CreatedAt TEXT, UpdatedAt TEXT, FolderId INTEGER);
                    CREATE TABLE QueryStream (Id INTEGER PRIMARY KEY, QueryId INTEGER, StreamId TEXT, StreamName TEXT);
                    INSERT INTO GraylogProfile VALUES (1, 'Ana', 'https://g/api', 'gAAAA', 1, 1, '', '');
                    INSERT INTO QueryFolder VALUES (1, 'Ops', NULL, 0);
                    INSERT INTO Query VALUES (1, 'Q', 1, 'a:{N}', 'Relative', 600, NULL, NULL, NULL, '["x"]', NULL, NULL, 150, '', '', 1);
                    INSERT INTO QueryStream VALUES (1, 1, 'sid', 'App');
                    """);
            }

            var doc = await LegacyDatabaseReader.ReadAsync(path, Ct);

            Assert.Equal("gAAAA", doc.Profiles.Single().TokenEncrypted);
            var q = doc.Queries.Single();
            Assert.Equal("Ops", q.Folder);
            Assert.Equal("Ana", q.ProfileName);
            Assert.Equal(["x"], q.Fields);
            Assert.Equal("App", q.Streams!.Single().StreamName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Legacy_reader_rejects_foreign_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gs1-{Guid.NewGuid():N}.db");
        try
        {
            await using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await c.OpenAsync(Ct);
                await c.ExecuteAsync("CREATE TABLE Other (Id INTEGER)");
            }
            await Assert.ThrowsAsync<ValidationException>(() => LegacyDatabaseReader.ReadAsync(path, Ct));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
