using System.Security.Cryptography;
using System.Text;
using GrayScope.Core.Abstractions;
using GrayScope.Core.Errors;

namespace GrayScope.Infrastructure.Security;

/// <summary>
/// Protects secrets for the current OS user.
/// <list type="bullet">
/// <item>Windows: DPAPI (CurrentUser scope) — no key file to leak or lose.</item>
/// <item>Other OS: AES-256-GCM with a random key in a 0600 key file under the data directory.</item>
/// </list>
/// Output is prefixed with the scheme so the format can evolve.
/// </summary>
public sealed class LocalSecretProtector(string keyFilePath) : ISecretProtector
{
    private const string DpapiPrefix = "dpapi:";
    private const string AesPrefix = "aesgcm:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    /// <summary>Binds DPAPI output to this application so other apps using DPAPI cannot trivially reuse it.</summary>
    private static readonly byte[] Entropy = "GrayScope2.TokenProtection.v1"u8.ToArray();

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var data = Encoding.UTF8.GetBytes(plaintext);
        if (OperatingSystem.IsWindows())
        {
            return DpapiPrefix + Convert.ToBase64String(ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser));
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(GetOrCreateKey(), TagSize))
        {
            aes.Encrypt(nonce, data, cipher, tag);
        }
        return AesPrefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedValue)
    {
        try
        {
            if (protectedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal) && OperatingSystem.IsWindows())
            {
                var raw = Convert.FromBase64String(protectedValue[DpapiPrefix.Length..]);
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser));
            }
            if (protectedValue.StartsWith(AesPrefix, StringComparison.Ordinal))
            {
                var raw = Convert.FromBase64String(protectedValue[AesPrefix.Length..]);
                if (raw.Length < NonceSize + TagSize)
                {
                    throw new CryptographicException("Protected value is truncated.");
                }
                var plain = new byte[raw.Length - NonceSize - TagSize];
                using var aes = new AesGcm(GetOrCreateKey(), TagSize);
                aes.Decrypt(raw.AsSpan(0, NonceSize), raw.AsSpan(NonceSize + TagSize), raw.AsSpan(NonceSize, TagSize), plain);
                return Encoding.UTF8.GetString(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new SecretUnavailableException("Token çözülemedi (farklı kullanıcı/bilgisayar veya bozuk veri). Token'ı yeniden girin.", ex);
        }
        throw new SecretUnavailableException("Token bu bilgisayarda desteklenmeyen bir biçimde saklanmış. Token'ı yeniden girin.");
    }

    private byte[] GetOrCreateKey()
    {
        if (File.Exists(keyFilePath))
        {
            var existing = File.ReadAllBytes(keyFilePath);
            if (existing.Length == KeySize)
            {
                return existing;
            }
            throw new SecretUnavailableException("Anahtar dosyası bozuk.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath)!);
        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(keyFilePath, key);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return key;
    }
}
