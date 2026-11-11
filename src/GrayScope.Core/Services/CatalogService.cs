using System.Collections.Concurrent;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;

namespace GrayScope.Core.Services;

/// <summary>
/// Per-profile cache of stream and field lists. Shared by every tab and dialog so a
/// profile's metadata is fetched once per session unless the user refreshes it.
/// </summary>
public sealed class CatalogService(IGraylogClientFactory clientFactory)
{
    private readonly ConcurrentDictionary<long, IReadOnlyList<StreamInfo>> _streams = new();
    private readonly ConcurrentDictionary<long, IReadOnlyList<string>> _fields = new();

    public async Task<IReadOnlyList<StreamInfo>> GetStreamsAsync(GraylogProfile profile, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _streams.TryGetValue(profile.Id, out var cached))
        {
            return cached;
        }
        var streams = await clientFactory.Create(profile).GetStreamsAsync(ct).ConfigureAwait(false);
        var ordered = streams.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        _streams[profile.Id] = ordered;
        return ordered;
    }

    public async Task<IReadOnlyList<string>> GetFieldsAsync(GraylogProfile profile, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _fields.TryGetValue(profile.Id, out var cached))
        {
            return cached;
        }
        var fields = await clientFactory.Create(profile).GetFieldNamesAsync(ct).ConfigureAwait(false);
        var ordered = fields.Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList();
        _fields[profile.Id] = ordered;
        return ordered;
    }

    /// <summary>Returns cached fields without a network call (empty if not loaded yet).</summary>
    public IReadOnlyList<string> PeekFields(long profileId) => _fields.TryGetValue(profileId, out var f) ? f : [];

    public void Invalidate(long profileId)
    {
        _streams.TryRemove(profileId, out _);
        _fields.TryRemove(profileId, out _);
    }
}
