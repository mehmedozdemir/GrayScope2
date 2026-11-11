using GrayScope.Core.Domain;
using GrayScope.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace GrayScope.Infrastructure.Tests.Persistence;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"gs2-test-{Guid.NewGuid():N}.db");
    private readonly Database _db;

    public RepositoryTests()
    {
        _db = new Database(_path);
        _db.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<GraylogProfile> AddProfileAsync(string name = "Ana", bool isDefault = false) =>
        await new ProfileRepository(_db).SaveAsync(new GraylogProfile { Name = name, BaseUrl = "https://g/api", TokenProtected = "dpapi:x", IsDefault = isDefault }, Ct);

    [Fact]
    public void Migrate_is_idempotent()
    {
        _db.Migrate();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        Assert.Equal((long)Database.LatestVersion, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public async Task Only_one_profile_stays_default()
    {
        await AddProfileAsync("A", isDefault: true);
        await AddProfileAsync("B", isDefault: true);

        var all = await new ProfileRepository(_db).GetAllAsync(Ct);

        Assert.Equal("B", Assert.Single(all, p => p.IsDefault).Name);
    }

    [Fact]
    public async Task Query_round_trips_with_streams_and_time_range()
    {
        var profile = await AddProfileAsync();
        var repo = new QueryRepository(_db);
        var from = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(3));
        var saved = await repo.SaveAsync(new SavedQuery
        {
            Name = "Q",
            ProfileId = profile.Id,
            Template = "a:{B}",
            TimeRange = new TimeRangeSpec(TimeRangeKind.Absolute, From: from, To: from.AddHours(1)),
            Fields = ["x", "y"],
            DefaultSortField = "x",
            DefaultSortDirection = SortDirection.Ascending,
            Streams = [new StreamRef("1", "S1"), new StreamRef("2", "S2")],
        }, Ct);

        saved.Streams = [new StreamRef("3", "S3")];
        await repo.SaveAsync(saved, Ct);
        var loaded = await repo.GetByIdAsync(saved.Id, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(from, loaded.TimeRange.From);
        Assert.Equal(["x", "y"], loaded.Fields);
        Assert.Equal(SortDirection.Ascending, loaded.DefaultSortDirection);
        Assert.Equal("S3", Assert.Single(loaded.Streams).StreamName);
    }

    [Fact]
    public async Task Deleting_folder_removes_subfolders_and_moves_queries_to_root()
    {
        var profile = await AddProfileAsync();
        var folders = new FolderRepository(_db);
        var parent = await folders.SaveAsync(new QueryFolder { Name = "P" }, Ct);
        var child = await folders.SaveAsync(new QueryFolder { Name = "C", ParentId = parent.Id }, Ct);
        var queries = new QueryRepository(_db);
        var q = await queries.SaveAsync(new SavedQuery { Name = "Q", ProfileId = profile.Id, Template = "x", FolderId = child.Id }, Ct);

        await folders.DeleteAsync(parent.Id, Ct);

        Assert.Empty(await folders.GetAllAsync(Ct));
        Assert.Null((await queries.GetByIdAsync(q.Id, Ct))!.FolderId);
    }

    [Fact]
    public async Task Run_history_is_pruned_and_latest_is_reported()
    {
        var profile = await AddProfileAsync();
        var q = await new QueryRepository(_db).SaveAsync(new SavedQuery { Name = "Q", ProfileId = profile.Id, Template = "x" }, Ct);
        var history = new RunHistoryRepository(_db);
        var start = DateTime.UtcNow;
        for (var i = 0; i < RunHistoryRepository.RetainPerQuery + 5; i++)
        {
            await history.AddAsync(new QueryRun { QueryId = q.Id, RunAt = start.AddSeconds(i), ResultCount = i, IsSuccess = true, Parameters = new() { ["N"] = "34" } }, Ct);
        }

        var recent = await history.GetRecentAsync(q.Id, 500, Ct);
        var latest = await history.GetLatestPerQueryAsync(Ct);

        Assert.Equal(RunHistoryRepository.RetainPerQuery, recent.Count);
        Assert.Equal(RunHistoryRepository.RetainPerQuery + 4, latest[q.Id].ResultCount);
        Assert.Equal("34", recent[0].Parameters["N"]);
    }

    [Fact]
    public async Task Settings_upsert()
    {
        var store = new SettingsStore(_db);
        await store.SetAsync("theme", "dark", Ct);
        await store.SetAsync("theme", "light", Ct);
        Assert.Equal("light", await store.GetAsync("theme", Ct));
        Assert.Null(await store.GetAsync("missing", Ct));
    }
}
