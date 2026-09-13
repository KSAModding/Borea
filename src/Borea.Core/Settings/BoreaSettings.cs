using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Settings;

/// <summary>
/// Borea's own cross-platform configuration. No app level settings
/// are stored in this class.
/// </summary>
public sealed class BoreaSettings
{
    public string? GameDirectoryPath { get; }

    /// <summary>
    /// Installation state for each loader, keyed by loader id. Empty means none.
    /// </summary>
    public IReadOnlyDictionary<string, LoaderInstallation> LoaderInstallations { get; }

    /// <summary>Which release statuses Borea offers when it picks a release. Stable by default.</summary>
    public ReleaseChannel ReleaseChannel { get; }

    public BoreaSettings(
        string? gameDirectoryPath,
        IReadOnlyDictionary<string, LoaderInstallation>? loaderInstallations = null,
        ReleaseChannel releaseChannel = ReleaseChannel.Stable)
    {
        if (gameDirectoryPath is not null && string.IsNullOrWhiteSpace(gameDirectoryPath))
            throw new ArgumentException("Game directory path, if provided, cannot be whitespace.", nameof(gameDirectoryPath));

        if (!Enum.IsDefined(releaseChannel))
            throw new ArgumentOutOfRangeException(nameof(releaseChannel), releaseChannel, "The release channel is not defined.");

        GameDirectoryPath = gameDirectoryPath;
        LoaderInstallations = Build(loaderInstallations, nameof(loaderInstallations));
        ReleaseChannel = releaseChannel;
    }

    /// <summary>
    /// A copy with the game directory replaced. The other settings stay as they are.
    /// </summary>
    public BoreaSettings WithGameDirectory(string? gameDirectoryPath)
        => new(gameDirectoryPath, LoaderInstallations, ReleaseChannel);

    /// <summary>A copy with the release channel replaced. The other settings stay as they are.</summary>
    public BoreaSettings WithReleaseChannel(ReleaseChannel releaseChannel)
        => new(GameDirectoryPath, LoaderInstallations, releaseChannel);

    /// <summary>
    /// A copy with one loader installation set. The id is stored as given here,
    /// also when the loader was known under another casing.
    /// </summary>
    public BoreaSettings WithLoaderInstallation(string loaderId, LoaderInstallation installation)
    {
        ModIds.Validate(loaderId, nameof(loaderId));
        ArgumentNullException.ThrowIfNull(installation);

        // An assignment through the indexer keeps the key that is already
        // there, so the old casing goes first.
        var installations = new Dictionary<string, LoaderInstallation>(LoaderInstallations, ModIds.Comparer);
        installations.Remove(loaderId);
        installations[loaderId] = installation;

        return new BoreaSettings(GameDirectoryPath, installations, ReleaseChannel);
    }

    /// <summary>A copy without one loader installation. The other settings stay as they are.</summary>
    public BoreaSettings WithoutLoaderInstallation(string loaderId)
    {
        ModIds.Validate(loaderId, nameof(loaderId));

        var installations = new Dictionary<string, LoaderInstallation>(LoaderInstallations, ModIds.Comparer);
        installations.Remove(loaderId);

        return new BoreaSettings(GameDirectoryPath, installations, ReleaseChannel);
    }

    private static IReadOnlyDictionary<string, LoaderInstallation> Build(
        IReadOnlyDictionary<string, LoaderInstallation>? installations,
        string paramName)
    {
        var built = new Dictionary<string, LoaderInstallation>(ModIds.Comparer);

        foreach (var (loaderId, installation) in installations ?? new Dictionary<string, LoaderInstallation>())
        {
            ModIds.Validate(loaderId, paramName);

            if (installation is null)
                throw new ArgumentException($"The installation for loader '{loaderId}' cannot be null.", paramName);

            if (!built.TryAdd(loaderId, installation))
                throw new ArgumentException($"Loader id '{loaderId}' appears more than once when compared case-insensitively.", paramName);
        }

        return built;
    }
}
