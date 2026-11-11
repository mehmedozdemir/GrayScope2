using GrayScope.Core.Abstractions;
using GrayScope.Core.Domain;
using GrayScope.Core.Errors;

namespace GrayScope.Core.Services;

/// <summary>Graylog profile management: validation, token protection, delete guard and connection test.</summary>
public sealed class ProfileService(
    IProfileRepository profiles,
    IQueryRepository queries,
    ISecretProtector protector,
    IGraylogClientFactory clientFactory,
    CatalogService catalog)
{
    public Task<IReadOnlyList<GraylogProfile>> GetAllAsync(CancellationToken ct = default) => profiles.GetAllAsync(ct);

    public async Task<IReadOnlyList<GraylogProfile>> GetActiveAsync(CancellationToken ct = default) =>
        (await profiles.GetAllAsync(ct).ConfigureAwait(false)).Where(p => p.IsActive).ToList();

    /// <summary>
    /// Saves a profile. <paramref name="newToken"/> null/empty keeps the stored token (edit without retyping).
    /// </summary>
    public async Task<GraylogProfile> SaveAsync(GraylogProfile profile, string? newToken, CancellationToken ct = default)
    {
        profile.Name = profile.Name.Trim();
        if (profile.Name.Length == 0)
        {
            throw new ValidationException("Profil adı boş olamaz.");
        }
        profile.BaseUrl = NormalizeBaseUrl(profile.BaseUrl);

        var all = await profiles.GetAllAsync(ct).ConfigureAwait(false);
        if (all.Any(p => p.Id != profile.Id && string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException($"'{profile.Name}' adında bir profil zaten var.");
        }
        if (!string.IsNullOrWhiteSpace(newToken))
        {
            profile.TokenProtected = protector.Protect(newToken.Trim());
        }
        else if (profile.Id == 0)
        {
            throw new ValidationException("Yeni profil için API token girin.");
        }

        var saved = await profiles.SaveAsync(profile, ct).ConfigureAwait(false);
        catalog.Invalidate(saved.Id);
        return saved;
    }

    public async Task DeleteAsync(GraylogProfile profile, CancellationToken ct = default)
    {
        var count = await queries.CountByProfileAsync(profile.Id, ct).ConfigureAwait(false);
        if (count > 0)
        {
            throw new ValidationException($"Bu profile bağlı {count} sorgu var; önce onları silin veya başka profile taşıyın.");
        }
        await profiles.DeleteAsync(profile.Id, ct).ConfigureAwait(false);
        catalog.Invalidate(profile.Id);
    }

    /// <summary>Tests a server with a plaintext token (unsaved form) or the stored one when token is empty.</summary>
    public Task<GraylogSystemInfo> TestConnectionAsync(string baseUrl, string? plaintextToken, GraylogProfile? stored, CancellationToken ct = default)
    {
        var url = NormalizeBaseUrl(baseUrl);
        var client = !string.IsNullOrWhiteSpace(plaintextToken)
            ? clientFactory.Create(url, plaintextToken.Trim())
            : stored is { NeedsToken: false }
                ? clientFactory.Create(new GraylogProfile { Id = stored.Id, Name = stored.Name, BaseUrl = url, TokenProtected = stored.TokenProtected })
                : throw new ValidationException("Bağlantıyı test etmek için token girin.");
        return client.GetSystemInfoAsync(ct);
    }

    /// <summary>Accepts <c>https://host:9000</c>, <c>.../api</c> or <c>.../api/</c>; always returns <c>.../api</c>.</summary>
    public static string NormalizeBaseUrl(string? raw)
    {
        var value = (raw ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ValidationException("Sunucu adresi http:// veya https:// ile başlayan geçerli bir adres olmalı.");
        }
        return value.EndsWith("/api", StringComparison.OrdinalIgnoreCase) ? value : value + "/api";
    }

    /// <summary>Masks a secret for display/logging, e.g. <c>****1a2b</c>.</summary>
    public static string MaskToken(string? token) =>
        string.IsNullOrEmpty(token) || token.Length <= 4 ? "****" : "****" + token[^4..];
}
