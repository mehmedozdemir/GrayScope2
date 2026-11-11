using GrayScope.Core.Abstractions;
using GrayScope.Core.Errors;

namespace GrayScope.Core.Services;

/// <summary>Provides every field of a result row, fetching lazily from Graylog when the search returned only selected columns.</summary>
public sealed class MessageDetailService(IGraylogClientFactory clientFactory)
{
    /// <summary>Field list plus a note explaining when it is incomplete.</summary>
    public sealed record Detail(IReadOnlyDictionary<string, string> Fields, string? Notice);

    public async Task<Detail> GetAsync(ResultRecord row, CancellationToken ct = default)
    {
        var message = row.Message;
        if (message.HasAllFields || string.IsNullOrEmpty(message.MessageId))
        {
            return new Detail(message.Fields, message.HasAllFields ? null : "Mesaj kimliği bulunamadı; yalnızca sorgu alanları gösteriliyor.");
        }

        try
        {
            var client = clientFactory.Create(row.Source.Profile);
            var full = await client.GetMessageAsync(message.MessageId, message.Index, ct).ConfigureAwait(false);
            return full is { Count: > 0 }
                ? new Detail(full, null)
                : new Detail(message.Fields, "Sunucu tüm alanları döndürmedi; yalnızca sorgu alanları gösteriliyor.");
        }
        catch (GrayScopeException ex)
        {
            return new Detail(message.Fields, $"Tüm alanlar alınamadı: {ex.Message}");
        }
    }
}
