namespace Borea.Core.Settings;

/// <summary>
/// Borea's own cross-platform configuration. No app level settings
/// are stored in this class.
/// </summary>
public sealed class BoreaSettings
{
    public string? GameDirectoryPath { get; }
    public string? StarMapDirectoryPath { get; }

    public BoreaSettings(string? gameDirectoryPath, string? starMapDirectoryPath)
    {
        if (gameDirectoryPath is not null && string.IsNullOrWhiteSpace(gameDirectoryPath))
            throw new ArgumentException("Game directory path, if provided, cannot be whitespace.", nameof(gameDirectoryPath));

        if (starMapDirectoryPath is not null && string.IsNullOrWhiteSpace(starMapDirectoryPath))
            throw new ArgumentException("StarMap directory path, if provided, cannot be whitespace.", nameof(starMapDirectoryPath));

        GameDirectoryPath = gameDirectoryPath;
        StarMapDirectoryPath = starMapDirectoryPath;
    }
}