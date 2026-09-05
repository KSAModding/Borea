using System;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Paths;

/// <summary>
/// Resolves Borea's own %LocalAppData%-rooted paths directly, and KSA and mod loader paths from the provided settings.
/// </summary>
public sealed class GamePathProvider : IGamePathProvider
{
    private readonly string _boreaRoot;
    private readonly string? _gameDirectory;
    private readonly IReadOnlyDictionary<string, string> _loaderDirectories;

    public GamePathProvider(string? gameDirectory, IReadOnlyDictionary<string, string>? loaderDirectories = null)
    {
        if (gameDirectory is not null && string.IsNullOrWhiteSpace(gameDirectory))
            throw new ArgumentException("Game directory, if provided, cannot be whitespace.", nameof(gameDirectory));

        // Same rule as BoreaSettings, since this constructor is public too.
        var byId = new Dictionary<string, string>(ModIds.Comparer);
        foreach (var (loaderId, path) in loaderDirectories ?? new Dictionary<string, string>())
        {
            ModIds.Validate(loaderId, nameof(loaderDirectories));

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException($"The directory for loader '{loaderId}' cannot be whitespace.", nameof(loaderDirectories));

            if (!byId.TryAdd(loaderId, path))
                throw new ArgumentException($"Loader id '{loaderId}' appears more than once when compared case-insensitively.", nameof(loaderDirectories));
        }

        _gameDirectory = gameDirectory;
        _loaderDirectories = byId;
        _boreaRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Borea");
    }

    public string GetIndexPath() => Path.Combine(_boreaRoot, "index.json");
    public string GetInstancesRoot() => Path.Combine(_boreaRoot, "Instances");
    public string GetInstanceRoot(Guid instanceId) => Path.Combine(GetInstancesRoot(), instanceId.ToString());
    public string GetInstanceModsFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "mods");
    public string GetInstanceSavesFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "saves");
    public string GetInstanceVehiclesFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "vehicles");
    public string GetInstanceSettingsPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "settings.toml");
    public string GetInstanceManifestPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "manifest.toml");
    public string GetInstanceMetadataPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "instance.toml");
    public string GetActiveInstancePointerPath() => Path.Combine(_boreaRoot, "active-instance.toml");
    public string GetModFavoritesPath() => Path.Combine(_boreaRoot, "mod-favorites.toml");
    public string GetModPackFavoritesPath() => Path.Combine(_boreaRoot, "modpack-favorites.toml");
    public string GetBoreaSettingsPath() => Path.Combine(_boreaRoot, "borea-settings.toml");
    public string? GetGameDirectoryPath() => _gameDirectory;

    public string? GetLoaderDirectoryPath(string loaderId)
    {
        if (string.IsNullOrWhiteSpace(loaderId))
            throw new ArgumentException("Loader id cannot be null or whitespace.", nameof(loaderId));

        return _loaderDirectories.TryGetValue(loaderId, out var path) ? path : null;
    }
}
