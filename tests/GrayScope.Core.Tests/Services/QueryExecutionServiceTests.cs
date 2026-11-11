using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Services;

namespace GrayScope.Core.Tests.Services;

public class QueryExecutionServiceTests
{
    private readonly FakeClientFactory _factory = new();
    private readonly NullHistory _history = new();
    private readonly GraylogProfile _main = new() { Id = 1, Name = "Ana", BaseUrl = "https://a/api", TokenProtected = "x" };
    private readonly GraylogProfile _backup = new() { Id = 2, Name = "Yedek", BaseUrl = "https://b/api", TokenProtected = "x" };

    private QueryExecutionService CreateService() => new(_factory, new CatalogService(_factory), _history);

    private static SavedQuery Query() => new()
    {
        Id = 7,
        Name = "Hatalar",
        ProfileId = 1,
        Template = "NetworkId:{NetworkId}",
        Fields = ["source", "message"],
        Streams = [new StreamRef("s-main", "Uygulama")],
    };

    private static ExecutionRequest Request(SavedQuery q, string networkId, bool batch, params GraylogProfile[] profiles) => new()
    {
        Query = q,
        Template = q.Template,
        Parameters = new Dictionary<string, string> { ["NetworkId"] = networkId },
        IsBatch = batch,
        Profiles = profiles,
        TimeRange = TimeRangeSpec.LastMinutes(5),
        Size = 100,
    };

    [Fact]
    public async Task Single_run_renders_query_and_records_history()
    {
        var client = new FakeGraylogClient
        {
            OnSearch = r => new SearchResponse([Messages.Of("m1", "2026-01-01T10:00:00.000Z", ("source", "web"), ("message", "boom"))], GraylogApiFlavor.Scripting),
        };
        _factory.Clients[1] = client;

        var table = await CreateService().ExecuteAsync(Request(Query(), "34", batch: false, _main), TestContext.Current.CancellationToken);

        Assert.Equal("NetworkId:34", client.Searches.Single().Query);
        Assert.Equal(["s-main"], client.Searches.Single().StreamIds);
        Assert.Equal(["source", "message"], table.Columns);
        Assert.Equal(["web", "boom"], table.Rows.Single().Cells);
        Assert.Equal(1, _history.Runs.Single().ResultCount);
    }

    [Fact]
    public async Task Batch_run_fans_out_and_adds_context_columns()
    {
        var client = new FakeGraylogClient
        {
            OnSearch = r => new SearchResponse([Messages.Of(r.Query, "2026-01-01T10:00:00.000Z", ("source", r.Query))], GraylogApiFlavor.Scripting),
        };
        _factory.Clients[1] = client;

        var table = await CreateService().ExecuteAsync(Request(Query(), "34,35,6", batch: true, _main), TestContext.Current.CancellationToken);

        Assert.Equal(3, client.Searches.Count);
        Assert.Equal("{NetworkId}", table.Columns[0]);
        Assert.Equal(3, table.Rows.Count);
        Assert.Contains(table.Rows, r => r.Cells[0] == "35");
    }

    [Fact]
    public async Task Batch_mode_off_keeps_commas_in_value()
    {
        var client = new FakeGraylogClient();
        _factory.Clients[1] = client;

        await CreateService().ExecuteAsync(Request(Query(), "34,35", batch: false, _main), TestContext.Current.CancellationToken);

        Assert.Equal("NetworkId:34,35", client.Searches.Single().Query);
    }

    [Fact]
    public async Task Other_server_maps_streams_by_name_and_reports_missing_ones_as_error()
    {
        _factory.Clients[1] = new FakeGraylogClient();
        var other = new FakeGraylogClient();
        other.Streams.Add(new StreamInfo("s-other", "Uygulama"));
        _factory.Clients[2] = other;

        var table = await CreateService().ExecuteAsync(Request(Query(), "34", batch: false, _main, _backup), TestContext.Current.CancellationToken);

        Assert.Equal(["s-other"], other.Searches.Single().StreamIds);
        Assert.Equal(ResultTableBuilder.ServerColumn, table.Columns[0]);
        Assert.Equal(0, table.FailedCount);
    }

    [Fact]
    public async Task Other_server_without_matching_streams_fails_that_target_only()
    {
        var mainClient = new FakeGraylogClient
        {
            OnSearch = _ => new SearchResponse([Messages.Of("m1", "2026-01-01T10:00:00.000Z")], GraylogApiFlavor.Scripting),
        };
        _factory.Clients[1] = mainClient;
        var other = new FakeGraylogClient();
        _factory.Clients[2] = other;

        var table = await CreateService().ExecuteAsync(Request(Query(), "34", batch: false, _main, _backup), TestContext.Current.CancellationToken);

        Assert.Empty(other.Searches);
        Assert.True(table.IsPartialFailure);
        Assert.Single(table.Rows);
    }

    [Fact]
    public async Task Graylog_errors_are_captured_per_target()
    {
        _factory.Clients[1] = new FakeGraylogClient { OnSearch = _ => throw new GraylogTimeoutException("zaman aşımı") };

        var table = await CreateService().ExecuteAsync(Request(Query(), "34", batch: false, _main), TestContext.Current.CancellationToken);

        Assert.True(table.IsTotalFailure);
        Assert.Equal("zaman aşımı", table.Outcomes.Single().Error);
        Assert.False(_history.Runs.Single().IsSuccess);
    }

    [Fact]
    public async Task Missing_parameter_is_rejected_before_any_request()
    {
        var client = new FakeGraylogClient();
        _factory.Clients[1] = client;

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().ExecuteAsync(Request(Query(), "  ", batch: false, _main), TestContext.Current.CancellationToken));
        Assert.Empty(client.Searches);
    }

    [Fact]
    public async Task Profile_without_token_is_rejected()
    {
        var noToken = new GraylogProfile { Id = 3, Name = "Boş", TokenProtected = "" };
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().ExecuteAsync(Request(Query(), "34", batch: false, noToken), TestContext.Current.CancellationToken));
    }
}
