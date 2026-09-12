using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// The loader installation Borea records for launch, update, and uninstall.
/// </summary>
public sealed class LoaderInstallation
{
    public string DirectoryPath { get; }

    /// <summary>The matched indexed release, or null when it is unknown.</summary>
    public ModVersion? Version { get; }

    /// <summary>The file version read from the launch target, if available.</summary>
    public string? RawVersion { get; }

    /// <summary>True when Borea did not create the loader directory.</summary>
    public bool IsAdopted { get; }

    public LoaderInstallation(string directoryPath, ModVersion? version, string? rawVersion, bool isAdopted)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Loader directory path cannot be null or whitespace.", nameof(directoryPath));

        if (rawVersion is not null && string.IsNullOrWhiteSpace(rawVersion))
            throw new ArgumentException("Raw loader version, if provided, cannot be whitespace.", nameof(rawVersion));

        DirectoryPath = directoryPath;
        Version = version;
        RawVersion = rawVersion;
        IsAdopted = isAdopted;
    }
}
