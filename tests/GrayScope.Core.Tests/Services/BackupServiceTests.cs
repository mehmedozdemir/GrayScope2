using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Services;

namespace GrayScope.Core.Tests.Services;

public class BackupServiceTests
{
    private sealed class StubLegacy(string? result) : ILegacyTokenDecryptor
    {
        public string? TryDecrypt(string fernetToken) => result;
    }

    private readonly InMemoryProfiles _profiles = new();
    private readonly InMemoryFolders _folders = new();
    private readonly InMemoryQueries _queries = new();

    private BackupService Create(string? legacyResult = null) =>
        new(_profiles, _folders, _queries, new PrefixProtector(), new StubLegacy(legacyResult));

    private const string LegacyV2 = """
        {
          "version": 2,
          "profiles": [{"name": "Ana", "base_url": "https://g/api", "token_encrypted": "gAAAA...", "is_default": true, "is_active": true}],
          "folders": [{"name": "Ops", "parent_name": null}, {"name": "HTTP", "parent_name": "Ops"}],
          "queries": [{
            "name": "200 dışı", "folder": "HTTP", "profile_name": "Ana", "query_template": "NetworkId:{NetworkId}",
            "time_range_type": "Relative", "time_range_seconds": 3600, "fields": ["source"],
            "default_sort_field": "source", "default_sort_order": "asc", "result_size": 0,
            "streams": [{"stream_id": "abc", "stream_name": "App"}]
          }, {
            "name": "Kayıp", "profile_name": "Yok", "query_template": "x", "time_range_type": "Keyword", "time_range_keyword": "yesterday"
          }]
        }
        """;

    [Fact]
    public async Task Import_v2_recovers_fernet_token_and_folder_tree()
    {
        var summary = await Create(legacyResult: "plain-token").ImportAsync(LegacyV2, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.ProfilesAdded);
        Assert.Equal(1, summary.QueriesAdded);
        Assert.Equal(1, summary.QueriesSkipped);
        Assert.Equal("p:plain-token", _profiles.Items.Single().TokenProtected);

        var query = _queries.Items.Single();
        var folder = _folders.Items.Single(f => f.Id == query.FolderId);
        Assert.Equal("HTTP", folder.Name);
        Assert.Equal("Ops", _folders.Items.Single(f => f.Id == folder.ParentId).Name);
        Assert.Equal(3600, query.TimeRange.RangeSeconds);
        Assert.Equal(SortDirection.Ascending, query.DefaultSortDirection);
        Assert.Equal(0, query.ResultSize);
        Assert.Equal("abc", query.Streams.Single().StreamId);
    }

    [Fact]
    public async Task Import_without_decryptable_token_creates_profile_needing_token()
    {
        var summary = await Create(legacyResult: null).ImportAsync(LegacyV2, TestContext.Current.CancellationToken);

        Assert.True(_profiles.Items.Single().NeedsToken);
        Assert.Contains(summary.Warnings, w => w.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Export_then_import_round_trips_into_empty_store()
    {
        var source = Create();
        _profiles.Items.Add(new GraylogProfile { Id = 1, Name = "Ana", BaseUrl = "https://g/api", TokenProtected = "p:t" });
        _folders.Items.Add(new QueryFolder { Id = 1, Name = "A" });
        _folders.Items.Add(new QueryFolder { Id = 2, Name = "B", ParentId = 1 });
        _queries.Items.Add(new SavedQuery { Id = 1, Name = "Q", ProfileId = 1, FolderId = 2, Template = "x", Fields = ["f"] });

        var json = await source.ExportAsync(includeTokens: true, TestContext.Current.CancellationToken);

        var target = new BackupServiceTests();
        var summary = await target.Create().ImportAsync(json, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.QueriesAdded);
        Assert.Equal("p:t", target._profiles.Items.Single().TokenProtected);
        var folderId = target._queries.Items.Single().FolderId;
        Assert.Equal("B", target._folders.Items.Single(f => f.Id == folderId).Name);
    }

    [Fact]
    public async Task Export_without_tokens_omits_them()
    {
        _profiles.Items.Add(new GraylogProfile { Id = 1, Name = "Ana", BaseUrl = "https://g/api", TokenProtected = "p:secret" });

        var json = await Create().ExportAsync(includeTokens: false, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_rejects_invalid_json_and_unknown_versions()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Create().ImportAsync("{not json", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ValidationException>(() => Create().ImportAsync("""{"version": 99}""", TestContext.Current.CancellationToken));
    }
}
