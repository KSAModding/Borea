using System;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Paths;

/// <summary>
/// Resolves Borea's own paths under its root, %LocalAppData%\Borea unless another
/// root is given, the Instances, Backups and Static Mod Files folders under the library folder,
/// which is the root unless another one is given, and KSA and mod loader paths
/// from the provided settings.
/// </summary>
public sealed class GamePathProvider : IGamePathProvider
{
    private readonly string _boreaRoot;
    private readonly string? _gameDirectory;
    private readonly IReadOnlyDictionary<string, string> _loaderDirectories;
    private readonly string _sharedProfileRoot;
    private readonly string _libraryFolder;

    public GamePathProvider(string? gameDirectory, IReadOnlyDictionary<string, string>? loaderDirectories = null, string? boreaRoot = null, string? sharedProfileRoot = null, string? libraryFolder = null)
    {
        if (gameDirectory is not null && string.IsNullOrWhiteSpace(gameDirectory))
            throw new ArgumentException("Game directory, if provided, cannot be whitespace.", nameof(gameDirectory));

        if (boreaRoot is not null && string.IsNullOrWhiteSpace(boreaRoot))
            throw new ArgumentException("Borea root, if provided, cannot be whitespace.", nameof(boreaRoot));

        if (sharedProfileRoot is not null && string.IsNullOrWhiteSpace(sharedProfileRoot))
            throw new ArgumentException("Shared profile root, if provided, cannot be whitespace.", nameof(sharedProfileRoot));

        if (libraryFolder is not null && string.IsNullOrWhiteSpace(libraryFolder))
            throw new ArgumentException("Library folder, if provided, cannot be whitespace.", nameof(libraryFolder));

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
        _boreaRoot = boreaRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "Borea");
        _sharedProfileRoot = sharedProfileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify), "My Games", "Kitten Space Agency");
        _libraryFolder = libraryFolder ?? _boreaRoot;
    }

    public string GetIndexPath() => Path.Combine(_boreaRoot, "index.json");
    public string GetAnnouncementsPath() => Path.Combine(_boreaRoot, "announcements.toml");
    public string GetListingSchemaPath() => Path.Combine(_boreaRoot, "authored.schema.json");
    public string GetImageCacheFolder() => Path.Combine(_boreaRoot, "ImageCache");
    public string GetGamePatchNotesFolder() => Path.Combine(_boreaRoot, "GamePatchNotes");
    public string GetInstancesRoot() => Path.Combine(_libraryFolder, "Instances");
    public string GetStaticModFilesRoot() => Path.Combine(_libraryFolder, "Static Mod Files");
    public string GetLoadersRoot() => Path.Combine(_boreaRoot, "Loaders");
    public string GetInstanceRoot(Guid instanceId) => Path.Combine(GetInstancesRoot(), instanceId.ToString());
    public string GetInstanceModsFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "mods");
    public string GetInstanceSavesFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "saves");
    public string GetInstanceVehiclesFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "Vehicles");
    public string GetInstanceSettingsPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "settings.toml");
    public string GetInstanceHudLayoutsFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "HUDLayouts");
    public string GetInstanceCrashDumpsFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "crashdumps");
    public string GetInstanceExportsFolder(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "exports");
    public string GetInstanceGameLogPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "logs", "KittenSpaceAgency.log");
    public string GetInstanceLaunchLogPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "logs", "borea-launch.log");
    public string GetInstancePlaytimePath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "playtime.toml");
    public string GetInstanceManifestPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "manifest.toml");
    public string GetInstanceMetadataPath(Guid instanceId) => Path.Combine(GetInstanceRoot(instanceId), "instance.toml");
    public string GetActiveInstancePointerPath() => Path.Combine(_boreaRoot, "active-instance.toml");
    public string GetModFavoritesPath() => Path.Combine(_boreaRoot, "mod-favorites.toml");
    public string GetModPackFavoritesPath() => Path.Combine(_boreaRoot, "modpack-favorites.toml");
    public string GetBoreaSettingsPath() => Path.Combine(_boreaRoot, "borea-settings.toml");
    public string GetAppPreferencesPath() => Path.Combine(_boreaRoot, "app-preferences.json");
    public string GetTaskHistoryPath() => Path.Combine(_boreaRoot, "task-history.json");
    public string GetAppLockPath() => Path.Combine(_boreaRoot, "app.lock");
    public string GetLogsFolder() => Path.Combine(_boreaRoot, "Logs");
    public string GetBackupsRoot() => Path.Combine(_libraryFolder, "Backups");
    public string? GetGameDirectoryPath() => _gameDirectory;
    public string GetSharedProfileRoot() => _sharedProfileRoot;

    public string? GetLoaderDirectoryPath(string loaderId)
    {
        if (string.IsNullOrWhiteSpace(loaderId))
            throw new ArgumentException("Loader id cannot be null or whitespace.", nameof(loaderId));

        return _loaderDirectories.TryGetValue(loaderId, out var path) ? path : null;
    }
}
