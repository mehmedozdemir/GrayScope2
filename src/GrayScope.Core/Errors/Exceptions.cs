namespace GrayScope.Core.Errors;

/// <summary>Base type for every expected, user-presentable failure. <see cref="Exception.Message"/> is Turkish UI text.</summary>
public abstract class GrayScopeException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>User input failed validation (missing parameter, illegal characters, ...).</summary>
public sealed class ValidationException(string message) : GrayScopeException(message);

/// <summary>A stored secret could not be decrypted (different machine/user, corrupted value).</summary>
public sealed class SecretUnavailableException(string message, Exception? inner = null) : GrayScopeException(message, inner);

/// <summary>Base for Graylog integration failures.</summary>
public abstract class GraylogException(string message, Exception? inner = null) : GrayScopeException(message, inner);

public sealed class GraylogConnectionException(string message, Exception? inner = null) : GraylogException(message, inner);

public sealed class GraylogTimeoutException(string message, Exception? inner = null) : GraylogException(message, inner);

public sealed class GraylogAuthException(string message) : GraylogException(message);

/// <summary>Graylog answered with an unexpected HTTP status; <see cref="Detail"/> carries the (token-free) response body.</summary>
public sealed class GraylogApiException(string message, int statusCode, string? detail) : GraylogException(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Detail { get; } = detail;
}
