using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Services;

namespace GrayScope.Core.Tests.Services;

public class ManagementServiceTests
{
    [Theory]
    [InlineData("https://graylog:9000", "https://graylog:9000/api")]
    [InlineData("https://graylog:9000/api/", "https://graylog:9000/api")]
    [InlineData(" http://g/API ", "http://g/API")]
    public void NormalizeBaseUrl_appends_api_suffix(string input, string expected)
    {
        Assert.Equal(expected, ProfileService.NormalizeBaseUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("graylog:9000")]
    [InlineData("ftp://g")]
    public void NormalizeBaseUrl_rejects_invalid_addresses(string input)
    {
        Assert.Throws<ValidationException>(() => ProfileService.NormalizeBaseUrl(input));
    }

    [Fact]
    public void MaskToken_shows_only_last_four_characters()
    {
        Assert.Equal("****1234", ProfileService.MaskToken("secret-1234"));
        Assert.Equal("****", ProfileService.MaskToken("abc"));
    }

    [Fact]
    public async Task Deleting_profile_with_queries_is_blocked()
    {
        var queries = new InMemoryQueries();
        queries.Items.Add(new SavedQuery { Id = 1, ProfileId = 5 });
        var factory = new FakeClientFactory();
        var service = new ProfileService(new InMemoryProfiles(), queries, new PrefixProtector(), factory, new CatalogService(factory));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.DeleteAsync(new GraylogProfile { Id = 5 }, TestContext.Current.CancellationToken));
        Assert.Contains("1 sorgu", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_profile_protects_token()
    {
        var profiles = new InMemoryProfiles();
        var factory = new FakeClientFactory();
        var service = new ProfileService(profiles, new InMemoryQueries(), new PrefixProtector(), factory, new CatalogService(factory));

        var saved = await service.SaveAsync(new GraylogProfile { Name = "A", BaseUrl = "https://g" }, "tok", TestContext.Current.CancellationToken);

        Assert.Equal("p:tok", saved.TokenProtected);
        Assert.Equal("https://g/api", saved.BaseUrl);
    }

    [Fact]
    public async Task Duplicate_creates_unique_name_and_copies_streams()
    {
        var queries = new InMemoryQueries();
        queries.Items.Add(new SavedQuery { Id = 1, Name = "Q", ProfileId = 1, Template = "x", Streams = [new StreamRef("s", "S")] });
        queries.Items.Add(new SavedQuery { Id = 2, Name = "Q (kopya)", ProfileId = 1, Template = "x" });
        var service = new QueryService(queries, new InMemoryFolders(), new NullHistory());

        var copy = await service.DuplicateAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal("Q (kopya) 2", copy.Name);
        Assert.Single(copy.Streams);
        Assert.NotEqual(1, copy.Id);
    }

    [Fact]
    public async Task Folder_cannot_be_moved_under_its_descendant()
    {
        var folders = new InMemoryFolders();
        folders.Items.Add(new QueryFolder { Id = 1, Name = "A" });
        folders.Items.Add(new QueryFolder { Id = 2, Name = "B", ParentId = 1 });
        var service = new QueryService(new InMemoryQueries(), folders, new NullHistory());

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SaveFolderAsync(new QueryFolder { Id = 1, Name = "A", ParentId = 2 }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(TimeRangeKind.Keyword, null)]
    [InlineData(TimeRangeKind.Absolute, null)]
    public void Invalid_time_ranges_are_rejected(TimeRangeKind kind, string? keyword)
    {
        Assert.Throws<ValidationException>(() => TimeRangeValidator.Validate(new TimeRangeSpec(kind, Keyword: keyword)));
    }
}
