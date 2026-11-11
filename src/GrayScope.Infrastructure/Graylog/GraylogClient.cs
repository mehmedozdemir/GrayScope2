using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;

namespace GrayScope.Infrastructure.Graylog;

public sealed record GraylogClientOptions
{
    /// <summary>Timeout for metadata calls (system, streams, fields, single message).</summary>
    public TimeSpan MetadataTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Timeout for searches, which can legitimately take longer on big windows.</summary>
    public TimeSpan SearchTimeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Graylog REST adapter. Builds/sends/parses HTTP only; no business rules.
/// The token lives only in this instance's Authorization header value and is never logged.
/// </summary>
internal sealed class GraylogClient(
    HttpClient http,
    string baseUrl,
    string token,
    GraylogClientOptions options,
    ConcurrentDictionary<string, GraylogApiFlavor> flavorCache) : IGraylogClient
{
    internal const string RequestedBy = "GrayScope2";

    /// <summary>Window used when looking a single message up by id without knowing its index.</summary>
    private const int MessageLookupRangeSeconds = 30 * 24 * 3600;

    private static readonly HashSet<string> InvalidIndexValues = new(StringComparer.OrdinalIgnoreCase) { "", "-", "null", "none" };
    private static readonly string[] IdentityFields = ["timestamp", "_id", "gl2_source_index"];
    private static readonly string[] DefaultFields = ["timestamp", "source", "message"];

    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly AuthenticationHeaderValue _auth = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{token}:token")));

    public async Task<GraylogSystemInfo> GetSystemInfoAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("system", options.MetadataTimeout, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        return new GraylogSystemInfo(Str(root, "version"), Str(root, "cluster_id"), Str(root, "hostname"));
    }

    public async Task<IReadOnlyList<StreamInfo>> GetStreamsAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("streams", options.MetadataTimeout, ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("streams", out var streams))
        {
            return [];
        }
        return streams.EnumerateArray()
            .Select(s => new StreamInfo(Str(s, "id") ?? "", Str(s, "title") ?? Str(s, "id") ?? ""))
            .Where(s => s.Id.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetFieldNamesAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("system/fields", options.MetadataTimeout, ct).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("fields", out var fields)
            ? fields.EnumerateArray().Select(f => f.GetString() ?? "").Where(f => f.Length > 0).ToList()
            : [];
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        if (flavorCache.TryGetValue(_baseUrl, out var flavor) && flavor == GraylogApiFlavor.Universal)
        {
            return await SearchUniversalAsync(request, ct).ConfigureAwait(false);
        }

        var scripting = await TrySearchScriptingAsync(request, ct).ConfigureAwait(false);
        if (scripting is not null)
        {
            flavorCache[_baseUrl] = GraylogApiFlavor.Scripting;
            return scripting;
        }

        flavorCache[_baseUrl] = GraylogApiFlavor.Universal;
        return await SearchUniversalAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetMessageAsync(string messageId, string? index, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(index) && !InvalidIndexValues.Contains(index))
        {
            return await GetMessageByIndexAsync(index, messageId, ct).ConfigureAwait(false);
        }

        // Graylog 5.x CSV reports the index as "-": locate it through the universal API first.
        var located = await LocateMessageAsync(messageId, ct).ConfigureAwait(false);
        if (located.Fields is { } fields && fields.ContainsKey("message"))
        {
            return fields;
        }
        if (located.FieldNames is { Count: > 0 } names)
        {
            var byFields = await FetchByFieldsAsync(messageId, names, ct).ConfigureAwait(false);
            if (byFields is not null)
            {
                return byFields;
            }
        }
        return located.Index is { } idx && !InvalidIndexValues.Contains(idx)
            ? await GetMessageByIndexAsync(idx, messageId, ct).ConfigureAwait(false)
            : null;
    }

    // ── Search: Graylog ≥ 5 scripting API ───────────────────────────────────────────

    /// <summary>Returns null when the endpoint does not exist (HTTP 404/405), signalling an older server.</summary>
    private async Task<SearchResponse?> TrySearchScriptingAsync(SearchRequest request, CancellationToken ct)
    {
        var requested = request.Fields.Count > 0 ? request.Fields : DefaultFields;
        var fields = requested.Concat(IdentityFields.Where(f => !requested.Contains(f))).ToList();
        var body = new Dictionary<string, object?>
        {
            ["query"] = request.Query,
            ["streams"] = request.StreamIds,
            ["timerange"] = TimeRangeBody(request.TimeRange),
            ["fields"] = fields,
        };
        if (request.Size > 0)
        {
            body["size"] = request.Size;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, Url("search/messages"))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        using var response = await SendAsync(message, "text/csv", options.SearchTimeout, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            return null;
        }
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var csv = CsvParser.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (csv.Count == 0)
        {
            return new SearchResponse([], GraylogApiFlavor.Scripting);
        }

        var header = csv[0].Select(h => h.StartsWith("field: ", StringComparison.Ordinal) ? h[7..] : h).ToArray();
        var messages = csv.Skip(1)
            .Where(r => r.Length > 1 || (r.Length == 1 && r[0].Length > 0))
            .Select(r =>
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < header.Length; i++)
                {
                    map[header[i]] = i < r.Length ? r[i] : "";
                }
                return new MessageRecord
                {
                    Fields = map,
                    MessageId = NullIfEmpty(map.GetValueOrDefault("_id")),
                    Index = NullIfEmpty(map.GetValueOrDefault("gl2_source_index")),
                    HasAllFields = false,
                };
            })
            .ToList();
        return new SearchResponse(messages, GraylogApiFlavor.Scripting, messages.Count);
    }

    internal static Dictionary<string, object?> TimeRangeBody(TimeRangeSpec range) => range.Kind switch
    {
        TimeRangeKind.Absolute => new() { ["type"] = "absolute", ["from"] = Iso(range.From), ["to"] = Iso(range.To) },
        TimeRangeKind.Keyword => new() { ["type"] = "keyword", ["keyword"] = range.Keyword ?? "" },
        _ => new() { ["type"] = "relative", ["range"] = range.RangeSeconds },
    };

    // ── Search: Graylog 2.x–4.x universal API ───────────────────────────────────────

    private async Task<SearchResponse> SearchUniversalAsync(SearchRequest request, CancellationToken ct)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("query", request.Query),
            new("limit", (request.Size > 0 ? request.Size : 10_000).ToString(CultureInfo.InvariantCulture)),
            new("sort", "timestamp:desc"),
        };
        if (request.StreamIds.Count > 0)
        {
            query.Add(new("filter", "streams:" + string.Join(',', request.StreamIds)));
        }

        var range = request.TimeRange;
        var path = range.Kind switch
        {
            TimeRangeKind.Absolute => "search/universal/absolute",
            TimeRangeKind.Keyword => "search/universal/keyword",
            _ => "search/universal/relative",
        };
        switch (range.Kind)
        {
            case TimeRangeKind.Absolute:
                query.Add(new("from", Iso(range.From) ?? ""));
                query.Add(new("to", Iso(range.To) ?? ""));
                break;
            case TimeRangeKind.Keyword:
                query.Add(new("keyword", range.Keyword ?? ""));
                break;
            default:
                query.Add(new("range", range.RangeSeconds.ToString(CultureInfo.InvariantCulture)));
                break;
        }

        using var doc = await GetJsonAsync(path + QueryString(query), options.SearchTimeout, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var messages = root.TryGetProperty("messages", out var list)
            ? list.EnumerateArray().Select(entry =>
            {
                var fields = entry.TryGetProperty("message", out var m) ? Flatten(m) : Flatten(entry);
                return new MessageRecord
                {
                    Fields = fields,
                    MessageId = NullIfEmpty(fields.GetValueOrDefault("_id")),
                    Index = Str(entry, "index"),
                    HasAllFields = true,
                };
            }).ToList()
            : [];
        long? total = root.TryGetProperty("total_results", out var t) && t.TryGetInt64(out var n) ? n : null;
        return new SearchResponse(messages, GraylogApiFlavor.Universal, total);
    }

    // ── Single message helpers ──────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<string, string>?> GetMessageByIndexAsync(string index, string messageId, CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync($"messages/{Uri.EscapeDataString(index)}/{Uri.EscapeDataString(messageId)}", options.MetadataTimeout, ct).ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("message", out var m) ? Flatten(m) : Flatten(doc.RootElement);
        }
        catch (GraylogApiException)
        {
            return null;
        }
    }

    private async Task<(IReadOnlyDictionary<string, string>? Fields, string? Index, IReadOnlyList<string>? FieldNames)> LocateMessageAsync(string messageId, CancellationToken ct)
    {
        try
        {
            var path = "search/universal/relative" + QueryString(
            [
                new("query", $"_id:\"{messageId}\""),
                new("range", MessageLookupRangeSeconds.ToString(CultureInfo.InvariantCulture)),
                new("limit", "1"),
            ]);
            using var doc = await GetJsonAsync(path, options.MetadataTimeout, ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("messages", out var list) || list.GetArrayLength() == 0)
            {
                return (null, null, null);
            }
            var entry = list[0];
            var fields = entry.TryGetProperty("message", out var m) ? Flatten(m) : Flatten(entry);
            IReadOnlyList<string>? names = null;
            if (entry.TryGetProperty("message", out var msg) && msg.TryGetProperty("field_names", out var fn) && fn.ValueKind == JsonValueKind.Array)
            {
                names = fn.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
            }
            return (fields, Str(entry, "index"), names);
        }
        catch (GraylogApiException)
        {
            return (null, null, null);
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> FetchByFieldsAsync(string messageId, IReadOnlyList<string> fields, CancellationToken ct)
    {
        var result = await TrySearchScriptingAsync(
            new SearchRequest($"_id:\"{messageId}\"", [], new TimeRangeSpec(TimeRangeKind.Relative, MessageLookupRangeSeconds), fields, 1),
            ct).ConfigureAwait(false);
        return result is { Messages.Count: > 0 } ? result.Messages[0].Fields : null;
    }

    // ── HTTP plumbing ───────────────────────────────────────────────────────────────

    private async Task<JsonDocument> GetJsonAsync(string relativePath, TimeSpan timeout, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, Url(relativePath));
        using var response = await SendAsync(message, "application/json", timeout, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new GraylogApiException("Graylog beklenmeyen bir yanıt döndürdü (JSON değil). Sunucu adresinin /api ile bittiğinden emin olun.", (int)response.StatusCode, ex.Message);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, string accept, TimeSpan timeout, CancellationToken ct)
    {
        message.Headers.Authorization = _auth;
        message.Headers.Accept.ParseAdd(accept);
        message.Headers.Add("X-Requested-By", RequestedBy);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new GraylogTimeoutException($"Graylog sunucusu {timeout.TotalSeconds:0} saniye içinde yanıt vermedi (zaman aşımı).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new GraylogConnectionException($"Graylog sunucusuna bağlanılamadı: {ex.Message}", ex);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new GraylogAuthException("Kimlik doğrulama başarısız: token geçersiz, süresi dolmuş veya yetkisi yetersiz. Profil ayarlarından token'ı güncelleyin.");
        }
        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        var apiMessage = TryReadApiError(body);
        var text = status == 400 && apiMessage is not null
            ? $"Sorgu hatası: {apiMessage}"
            : $"Graylog isteği başarısız (HTTP {status}){(apiMessage is null ? "" : ": " + apiMessage)}";
        throw new GraylogApiException(text, status, body.Length > 4000 ? body[..4000] : body);
    }

    private static string? TryReadApiError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Str(doc.RootElement, "message");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Uri Url(string relativePath) => new($"{_baseUrl}/{relativePath.TrimStart('/')}");

    private static string QueryString(IEnumerable<KeyValuePair<string, string>> parameters) =>
        "?" + string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static string? Iso(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()
            : null;

    internal static Dictionary<string, string> Flatten(JsonElement obj)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return map;
        }
        foreach (var p in obj.EnumerateObject())
        {
            map[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? "",
                JsonValueKind.Null => "",
                _ => p.Value.GetRawText(),
            };
        }
        return map;
    }
}

/// <summary>Creates clients sharing one connection pool; decrypts tokens only in memory.</summary>
public sealed class GraylogClientFactory : IGraylogClientFactory, IDisposable
{
    private readonly HttpClient _http;
    private readonly ISecretProtector _protector;
    private readonly GraylogClientOptions _options;
    private readonly ConcurrentDictionary<string, GraylogApiFlavor> _flavors = new(StringComparer.OrdinalIgnoreCase);

    public GraylogClientFactory(ISecretProtector protector, GraylogClientOptions? options = null, HttpMessageHandler? handler = null)
    {
        _protector = protector;
        _options = options ?? new GraylogClientOptions();
        _http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            // Per-request timeouts are enforced in GraylogClient.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public IGraylogClient Create(GraylogProfile profile)
    {
        if (profile.NeedsToken)
        {
            throw new ValidationException($"'{profile.Name}' profili için token tanımlı değil. Ayarlar'dan token girin.");
        }
        return Create(profile.BaseUrl, _protector.Unprotect(profile.TokenProtected));
    }

    public IGraylogClient Create(string baseUrl, string plaintextToken) =>
        new GraylogClient(_http, baseUrl, plaintextToken, _options, _flavors);

    public void Dispose() => _http.Dispose();
}
