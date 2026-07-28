using System;
using Borea.Core.Paths;

namespace Borea.Storage.Paths;

/// <summary>
/// Resolves Borea's own %LocalAppData%-rooted paths directly, and KSA and StarMap paths from the provided settings.
/// </summary>
public sealed class GamePathProvider : IGamePathProvider
{
    private readonly string _boreaRoot;
    private readonly string? _gameDirectory;
    private readonly string? _starMapDirectory;

    public GamePathProvider(string? gameDirectory, string? starMapDirectory)
    {
        if (gameDirectory is not null && string.IsNullOrWhiteSpace(gameDirectory))
            throw new ArgumentException("Game directory, if provided, cannot be whitespace.", nameof(gameDirectory));

        if (starMapDirectory is not null && string.IsNullOrWhiteSpace(starMapDirectory))
            throw new ArgumentException("StarMap directory, if provided, cannot be whitespace.", nameof(starMapDirectory));

        _gameDirectory = gameDirectory;
        _starMapDirectory = starMapDirectory;
        _boreaRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Borea");
    }

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
    public string? GetStarMapDirectoryPath() => _starMapDirectory;
}