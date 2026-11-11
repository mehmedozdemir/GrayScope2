using GrayScope.Core.Errors;
using GrayScope.Infrastructure.Security;

namespace GrayScope.Infrastructure.Tests.Security;

public class SecurityTests
{
    // Generated with Python cryptography.fernet (GrayScope 1's library) — proves wire compatibility.
    private const string FernetKey = "ta6YnBKoN4hkXfTCvRRbeag28GOUs_hLxXoYvrUlsQA=";
    private const string FernetToken = "gAAAAABq9BMHogI9dlh-iuazVhZA0g4YIrpcFLEpOWbfMTl699GtYLhWz2ce-BekArdo5KfovtGwvmGYkZBc1ihDarA-RnMEte-jc8uRhLoDnfyOn1WH510=";

    [Fact]
    public void Fernet_token_from_python_is_decrypted()
    {
        Assert.Equal("my-graylog-token-1234", new FernetTokenDecryptor(() => FernetKey).TryDecrypt(FernetToken));
    }

    [Fact]
    public void Fernet_tampered_token_or_wrong_key_returns_null()
    {
        var tampered = FernetToken[..^6] + "AAAAA=";
        Assert.Null(new FernetTokenDecryptor(() => FernetKey).TryDecrypt(tampered));
        Assert.Null(new FernetTokenDecryptor(() => "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=").TryDecrypt(FernetToken));
        Assert.Null(new FernetTokenDecryptor(() => null).TryDecrypt(FernetToken));
    }

    [Fact]
    public void Local_protector_round_trips_and_hides_plaintext()
    {
        var protector = new LocalSecretProtector(Path.Combine(Path.GetTempPath(), $"gs2-key-{Guid.NewGuid():N}"));

        var protectedValue = protector.Protect("token-abcd");

        Assert.DoesNotContain("token-abcd", protectedValue, StringComparison.Ordinal);
        Assert.Equal("token-abcd", protector.Unprotect(protectedValue));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("dpapi:not-base64!")]
    [InlineData("aesgcm:AAAA")]
    public void Local_protector_rejects_foreign_values(string value)
    {
        var protector = new LocalSecretProtector(Path.Combine(Path.GetTempPath(), $"gs2-key-{Guid.NewGuid():N}"));
        Assert.Throws<SecretUnavailableException>(() => protector.Unprotect(value));
    }
}
