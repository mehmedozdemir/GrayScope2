namespace GrayScope.Infrastructure;

/// <summary>
/// File-system locations. Overridable through <c>GRAYSCOPE2_DATA_DIR</c> so tests and portable
/// installs never touch the user's real database.
/// </summary>
public sealed class AppPaths
{
    public const string DataDirEnvironmentVariable = "GRAYSCOPE2_DATA_DIR";

    public AppPaths(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory
            ?? Environment.GetEnvironmentVariable(DataDirEnvironmentVariable)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GrayScope2");
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "grayscope2.db");
    public string KeyFilePath => Path.Combine(DataDirectory, "secret.key");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Default GrayScope 1 Fernet key location (<c>~/.grayscope/secret.key</c>).</summary>
    public static string LegacyKeyFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grayscope", "secret.key");
}
