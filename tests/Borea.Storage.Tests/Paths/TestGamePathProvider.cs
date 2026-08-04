using Borea.Core.Paths;

namespace Borea.Storage.Tests.Paths;

/// <summary>
/// Minimal IGamePathProvider rooted at an arbitrary folder — used to exercise
/// real disk I/O in tests without touching actual LocalAppData paths.
/// </summary>
internal sealed class TestGamePathProvider : IGamePathProvider
{
    private readonly string _root;

    public TestGamePathProvider(string root) => _root = root;

    public string GetInstancesRoot() => Path.Combine(_root, "Instances");
    public string GetInstanceRoot(Guid instanceId) => Path.Combine(GetInstancesRoot(), instanceId.ToString());
    public string GetInstanceModsFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "mods");
    public string GetInstanceSavesFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "saves");
    public string GetInstanceVehiclesFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "vehicles");
    public string GetInstanceSettingsPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "settings.toml");
    public string GetInstanceManifestPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "manifest.toml");
    public string GetInstanceMetadataPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "instance.toml");
    public string GetActiveInstancePointerPath() => Path.Combine(_root, "active-instance.toml");
    public string GetModFavoritesPath() => Path.Combine(_root, "mod-favorites.toml");
    public string GetModPackFavoritesPath() => Path.Combine(_root, "modpack-favorites.toml");
    public string GetBoreaSettingsPath() => Path.Combine(_root, "borea-settings.toml");
    public string GetGameDirectoryPath() => Path.Combine(_root, "Game");
    public string GetStarMapDirectoryPath() => Path.Combine(_root, "StarMap");
}
