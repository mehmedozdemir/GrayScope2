using System.Security.Cryptography;
using System.Text;
using GrayScope.Core.Services;

namespace GrayScope.Infrastructure.Security;

/// <summary>
/// Reads Fernet tokens written by GrayScope 1 (Python <c>cryptography.fernet</c>) so profiles can be
/// migrated without retyping tokens. Decrypt-only; GrayScope 2 never writes Fernet.
/// </summary>
/// <remarks>
/// Spec: https://github.com/fernet/spec/blob/master/Spec.md —
/// token = 0x80 ‖ timestamp(8) ‖ IV(16) ‖ AES-128-CBC ciphertext ‖ HMAC-SHA256(32);
/// key = signing key(16) ‖ encryption key(16), both base64url.
/// </remarks>
public sealed class FernetTokenDecryptor : ILegacyTokenDecryptor
{
    private const byte Version = 0x80;
    private const int HeaderSize = 1 + 8 + 16;
    private const int HmacSize = 32;

    private readonly Func<string?> _keyProvider;

    /// <param name="keyProvider">Returns the base64url Fernet key, or null when unavailable.</param>
    public FernetTokenDecryptor(Func<string?> keyProvider) => _keyProvider = keyProvider;

    /// <summary>Uses GrayScope 1's default key file (<c>~/.grayscope/secret.key</c>).</summary>
    public static FernetTokenDecryptor FromDefaultKeyFile(string path) =>
        new(() => File.Exists(path) ? File.ReadAllText(path).Trim() : null);

    public string? TryDecrypt(string fernetToken)
    {
        var keyText = _keyProvider();
        if (string.IsNullOrEmpty(keyText) || string.IsNullOrEmpty(fernetToken))
        {
            return null;
        }
        try
        {
            return Decrypt(fernetToken, keyText);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    internal static string Decrypt(string token, string base64UrlKey)
    {
        var key = FromBase64Url(base64UrlKey);
        if (key.Length != 32)
        {
            throw new CryptographicException("Invalid Fernet key length.");
        }
        var data = FromBase64Url(token);
        if (data.Length < HeaderSize + HmacSize + 16 || data[0] != Version)
        {
            throw new CryptographicException("Invalid Fernet token.");
        }

        var signed = data.AsSpan(0, data.Length - HmacSize);
        var expected = HMACSHA256.HashData(key.AsSpan(0, 16), signed);
        if (!CryptographicOperations.FixedTimeEquals(expected, data.AsSpan(data.Length - HmacSize)))
        {
            throw new CryptographicException("Fernet signature mismatch.");
        }

        using var aes = Aes.Create();
        aes.Key = key[16..];
        var iv = data.AsSpan(9, 16);
        var cipher = data.AsSpan(HeaderSize, data.Length - HeaderSize - HmacSize);
        var plain = aes.DecryptCbc(cipher, iv, PaddingMode.PKCS7);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Trim().Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Convert.FromBase64String(s);
    }
}
