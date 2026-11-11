using System.Diagnostics;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;
using GrayScope.Core.Querying;

namespace GrayScope.Core.Services;

/// <summary>
/// Orchestrates a run: parameter substitution, batch fan-out across parameter combinations and
/// servers, stream mapping between servers, bounded parallel execution and history recording.
/// </summary>
public sealed class QueryExecutionService(
    IGraylogClientFactory clientFactory,
    CatalogService catalog,
    IRunHistoryRepository history)
{
    /// <summary>Concurrent searches per run; keeps a batch from hammering a Graylog cluster.</summary>
    public const int MaxParallelSearches = 4;

    public async Task<ResultTable> ExecuteAsync(ExecutionRequest request, CancellationToken ct = default)
    {
        var plan = BuildPlan(request);
        var stopwatch = Stopwatch.StartNew();

        var streamMaps = new Dictionary<long, (IReadOnlyList<string> Ids, List<string> Warnings, string? Error)>();
        foreach (var profile in request.Profiles)
        {
            streamMaps[profile.Id] = await ResolveStreamsAsync(request.Query, profile, ct).ConfigureAwait(false);
        }

        using var gate = new SemaphoreSlim(MaxParallelSearches);
        var tasks = plan.Select(async target =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var streams = streamMaps[target.Profile.Id];
                return await RunTargetAsync(request, target, streams, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });
        var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
        stopwatch.Stop();

        var table = ResultTableBuilder.Build(request.Query, outcomes, stopwatch.Elapsed);
        await RecordHistoryAsync(request, table, ct).ConfigureAwait(false);
        return table;
    }

    /// <summary>Validates inputs and renders every target query up-front so no request is sent for an invalid plan.</summary>
    public static IReadOnlyList<(GraylogProfile Profile, IReadOnlyDictionary<string, string> Parameters, string Query)> BuildPlan(ExecutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Template))
        {
            throw new ValidationException("Sorgu metni boş olamaz.");
        }
        if (request.Profiles.Count == 0)
        {
            throw new ValidationException("En az bir Graylog sunucusu seçin.");
        }
        var missingToken = request.Profiles.FirstOrDefault(p => p.NeedsToken);
        if (missingToken is not null)
        {
            throw new ValidationException($"'{missingToken.Name}' profili için token tanımlı değil. Ayarlar'dan token girin.");
        }

        var declared = QueryTemplate.ExtractParameters(request.Template).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var relevant = request.Parameters
            .Where(p => declared.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        IReadOnlyList<IReadOnlyDictionary<string, string>> combos = request.IsBatch
            ? QueryTemplate.ExpandBatch(relevant)
            : [relevant];

        var targets = request.Profiles
            .SelectMany(profile => combos.Select(combo => (profile, combo, QueryTemplate.Render(request.Template, combo))))
            .ToList();
        if (targets.Count > QueryTemplate.MaxBatchCombinations)
        {
            throw new ValidationException($"Toplu çalıştırma en fazla {QueryTemplate.MaxBatchCombinations} arama yapabilir (şu an {targets.Count}).");
        }
        return targets;
    }

    private async Task<(IReadOnlyList<string> Ids, List<string> Warnings, string? Error)> ResolveStreamsAsync(
        SavedQuery query, GraylogProfile profile, CancellationToken ct)
    {
        var warnings = new List<string>();
        if (query.Streams.Count == 0 || profile.Id == query.ProfileId)
        {
            return (query.Streams.Select(s => s.StreamId).ToList(), warnings, null);
        }

        // Stream ids are server-specific; on another server match by stream title.
        IReadOnlyList<StreamInfo> available;
        try
        {
            available = await catalog.GetStreamsAsync(profile, ct: ct).ConfigureAwait(false);
        }
        catch (GrayScopeException ex)
        {
            return ([], warnings, $"Stream listesi alınamadı: {ex.Message}");
        }

        var byTitle = available.GroupBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var ids = new List<string>();
        foreach (var stream in query.Streams)
        {
            if (byTitle.TryGetValue(stream.StreamName, out var id))
            {
                ids.Add(id);
            }
            else
            {
                warnings.Add($"'{stream.StreamName}' stream'i bu sunucuda yok, atlandı.");
            }
        }

        // Silently widening to "all streams" would change the meaning of the query.
        return ids.Count == 0
            ? ([], warnings, "Sorgunun stream'lerinden hiçbiri bu sunucuda bulunamadı.")
            : (ids, warnings, null);
    }

    private async Task<TargetOutcome> RunTargetAsync(
        ExecutionRequest request,
        (GraylogProfile Profile, IReadOnlyDictionary<string, string> Parameters, string Query) target,
        (IReadOnlyList<string> Ids, List<string> Warnings, string? Error) streams,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (streams.Error is not null)
        {
            return new TargetOutcome(target.Profile, target.Parameters, target.Query, [], streams.Error, sw.Elapsed, streams.Warnings);
        }

        try
        {
            var client = clientFactory.Create(target.Profile);
            var search = new SearchRequest(target.Query, streams.Ids, request.TimeRange, request.Query.Fields, request.Size);
            var response = await client.SearchAsync(search, ct).ConfigureAwait(false);
            return new TargetOutcome(target.Profile, target.Parameters, target.Query, response.Messages, null, sw.Elapsed, streams.Warnings);
        }
        catch (GrayScopeException ex)
        {
            return new TargetOutcome(target.Profile, target.Parameters, target.Query, [], ex.Message, sw.Elapsed, streams.Warnings);
        }
    }

    private async Task RecordHistoryAsync(ExecutionRequest request, ResultTable table, CancellationToken ct)
    {
        if (request.Query.Id <= 0 || !request.RecordHistory)
        {
            return;
        }
        var firstError = table.Outcomes.FirstOrDefault(o => !o.IsSuccess)?.Error;
        await history.AddAsync(new QueryRun
        {
            QueryId = request.Query.Id,
            RunAt = DateTime.UtcNow,
            DurationMs = (int)table.Duration.TotalMilliseconds,
            ResultCount = table.Rows.Count,
            IsSuccess = !table.IsTotalFailure,
            ErrorMessage = firstError,
            Parameters = request.Parameters.ToDictionary(p => p.Key, p => p.Value),
        }, ct).ConfigureAwait(false);
    }
}
