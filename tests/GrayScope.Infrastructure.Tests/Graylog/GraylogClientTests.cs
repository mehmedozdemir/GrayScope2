using System.Net;
using System.Text;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Infrastructure.Graylog;

namespace GrayScope.Infrastructure.Tests.Graylog;

public class GraylogClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return await respond(request, cancellationToken);
        }
    }

    private sealed class NoopProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedValue) => protectedValue;
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static (IGraylogClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null) =>
        Create((r, _) => respond(r), timeout);

    private static (IGraylogClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        var handler = new StubHandler(respond);
        var options = new GraylogClientOptions { MetadataTimeout = timeout ?? TimeSpan.FromSeconds(5), SearchTimeout = timeout ?? TimeSpan.FromSeconds(5) };
        var factory = new GraylogClientFactory(new NoopProtector(), options, handler);
        return (factory.Create("https://g/api", "secret-token-9876"), handler);
    }

    private static SearchRequest Search(params string[] fields) =>
        new("NetworkId:34", ["s1"], TimeRangeSpec.LastMinutes(5), fields, 50);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scripting_search_parses_csv_and_sends_auth_and_identity_fields()
    {
        const string csv = "field: source,field: message,field: timestamp,field: _id,field: gl2_source_index\r\n" +
                           "web,\"hello, \"\"world\"\"\nline2\",2026-01-01T10:00:00.000Z,abc,-\r\n";
        var (client, handler) = Create(_ => Task.FromResult(Text(HttpStatusCode.OK, csv, "text/csv")));

        var response = await client.SearchAsync(Search("source", "message"), Ct);

        var (request, body) = handler.Requests.Single();
        Assert.Equal("https://g/api/search/messages", request.RequestUri!.ToString());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("GrayScope2", request.Headers.GetValues("X-Requested-By").Single());
        Assert.Contains("\"_id\"", body, StringComparison.Ordinal);
        Assert.Contains("\"size\":50", body, StringComparison.Ordinal);

        var message = Assert.Single(response.Messages);
        Assert.Equal("hello, \"world\"\nline2", message.Get("message"));
        Assert.Equal("abc", message.MessageId);
        Assert.Equal("-", message.Index);
        Assert.False(message.HasAllFields);
        Assert.Equal(GraylogApiFlavor.Scripting, response.Flavor);
    }

    [Fact]
    public async Task Search_falls_back_to_universal_api_on_404_and_remembers_it()
    {
        const string json = """{"total_results": 1, "messages": [{"index": "graylog_3", "message": {"_id": "m1", "source": "web", "level": 3, "tags": ["a"]}}]}""";
        var (client, handler) = Create(r => Task.FromResult(r.Method == HttpMethod.Post
            ? Text(HttpStatusCode.NotFound, "")
            : Text(HttpStatusCode.OK, json)));

        var first = await client.SearchAsync(Search("source"), Ct);
        await client.SearchAsync(Search("source"), Ct);

        Assert.Equal(GraylogApiFlavor.Universal, first.Flavor);
        var message = Assert.Single(first.Messages);
        Assert.True(message.HasAllFields);
        Assert.Equal("graylog_3", message.Index);
        Assert.Equal("3", message.Get("level"));
        Assert.Equal("[\"a\"]", message.Get("tags"));
        Assert.Single(handler.Requests, r => r.Request.Method == HttpMethod.Post);
        var universal = handler.Requests.Last().Request.RequestUri!.ToString();
        Assert.Contains("search/universal/relative", universal, StringComparison.Ordinal);
        Assert.Contains("range=300", universal, StringComparison.Ordinal);
        Assert.Contains("filter=streams%3As1", universal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Auth_failures_map_to_auth_exception_without_leaking_token(HttpStatusCode status)
    {
        var (client, _) = Create(_ => Task.FromResult(Text(status, "")));

        var ex = await Assert.ThrowsAsync<GraylogAuthException>(() => client.GetSystemInfoAsync(Ct));
        Assert.DoesNotContain("secret-token-9876", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_request_surfaces_graylog_error_message()
    {
        var (client, _) = Create(_ => Task.FromResult(Text(HttpStatusCode.BadRequest, """{"type":"ApiError","message":"Cannot parse 'a:('"}""")));

        var ex = await Assert.ThrowsAsync<GraylogApiException>(() => client.SearchAsync(Search(), Ct));

        Assert.Equal("Sorgu hatası: Cannot parse 'a:('", ex.Message);
    }

    [Fact]
    public async Task Slow_server_raises_timeout_exception()
    {
        var (client, _) = Create(async (r, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Text(HttpStatusCode.OK, "{}");
        }, timeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<GraylogTimeoutException>(() => client.GetSystemInfoAsync(Ct));
    }

    [Fact]
    public async Task Network_failure_raises_connection_exception()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("No such host"));

        await Assert.ThrowsAsync<GraylogConnectionException>(() => client.GetStreamsAsync(Ct));
    }

    [Fact]
    public async Task GetMessage_uses_index_endpoint_when_index_is_known()
    {
        var (client, handler) = Create(_ => Task.FromResult(Text(HttpStatusCode.OK, """{"message": {"_id": "m1", "full": "yes"}}""")));

        var fields = await client.GetMessageAsync("m1", "graylog_7", Ct);

        Assert.Equal("yes", fields!["full"]);
        Assert.EndsWith("/messages/graylog_7/m1", handler.Requests.Single().Request.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMessage_locates_index_when_csv_reported_dash()
    {
        var (client, _) = Create(r => Task.FromResult(r.RequestUri!.AbsolutePath.Contains("universal", StringComparison.Ordinal)
            ? Text(HttpStatusCode.OK, """{"messages": [{"index": "graylog_9", "message": {"_id": "m1", "message": "full text"}}]}""")
            : Text(HttpStatusCode.NotFound, "")));

        var fields = await client.GetMessageAsync("m1", "-", Ct);

        Assert.Equal("full text", fields!["message"]);
    }

    [Fact]
    public void Time_range_bodies_match_graylog_contract()
    {
        var from = new DateTimeOffset(2026, 1, 1, 13, 0, 0, TimeSpan.FromHours(3));
        var absolute = GraylogClient.TimeRangeBody(new TimeRangeSpec(TimeRangeKind.Absolute, From: from, To: from.AddHours(1)));
        var keyword = GraylogClient.TimeRangeBody(new TimeRangeSpec(TimeRangeKind.Keyword, Keyword: "yesterday"));

        Assert.Equal("2026-01-01T10:00:00.000Z", absolute["from"]);
        Assert.Equal("yesterday", keyword["keyword"]);
    }
}
