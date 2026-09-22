using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Toml;

namespace Borea.Storage.Game;

/// <summary>
/// Checks the installation and a profile against <see cref="GameAssumption"/>.
/// An assumption counts as broken only when the thing it is about is there and
/// has a different shape, so a game that was never started and an instance
/// without mods report nothing.
/// </summary>
public sealed class GameShapeCheck : IGameShapeCheck
{
    /// <summary>The names the game gives the folders of a profile.</summary>
    private static readonly string[] ProfileFolderNames = ["mods", "saves", "Vehicles", "logs"];

    private readonly IGamePathProvider _paths;
    private readonly IInstalledGameVersionProvider _version;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<GameAssumptionResult>? _installResults;
    private InstalledGameVersion? _installVersion;
    private string? _checkedBuild;

    public GameShapeCheck(IGamePathProvider paths, IInstalledGameVersionProvider version)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _version = version ?? throw new ArgumentNullException(nameof(version));
    }

    public async Task<GameShape> GetAsync(CancellationToken cancellationToken = default)
    {
        var (version, install) = await InstallAsync(cancellationToken).ConfigureAwait(false);
        var profile = await ProfileAsync(SharedProfile(), cancellationToken).ConfigureAwait(false);
        return new GameShape(version, VerifiedGameBuilds.Current, [.. install, .. profile]);
    }

    public async Task<GameShape> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var (version, install) = await InstallAsync(cancellationToken).ConfigureAwait(false);
        var profile = await ProfileAsync(Instance(instanceId), cancellationToken).ConfigureAwait(false);
        return new GameShape(version, VerifiedGameBuilds.Current, [.. install, .. profile]);
    }

    /// <summary>
    /// The installation, checked again only after the installed build changed.
    /// A build that cannot be read is a state of its own, so a game directory
    /// that is set later is checked then.
    /// </summary>
    private async Task<(InstalledGameVersion? Version, IReadOnlyList<GameAssumptionResult> Results)> InstallAsync(CancellationToken cancellationToken)
    {
        var version = _version.GetInstalledVersion();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_installResults is { } cached && string.Equals(_checkedBuild, version?.RawVersion, StringComparison.Ordinal))
                return (_installVersion, cached);

            var directory = _paths.GetGameDirectoryPath();
            var results = new List<GameAssumptionResult>
            {
                CheckGameAssembly(directory, version),
                await CheckContentManifestAsync(directory, cancellationToken).ConfigureAwait(false),
                CheckPatchNotes(directory),
            };

            _installResults = results;
            _installVersion = version;
            _checkedBuild = version?.RawVersion;
            return (version, results);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<IReadOnlyList<GameAssumptionResult>> ProfileAsync(ProfilePaths profile, CancellationToken cancellationToken)
    {
        // The manifest names the mods the profile is supposed to hold, so the
        // folder check reads it once here instead of reading the file again.
        var (manifest, listedMods) = await CheckProfileManifestAsync(profile, cancellationToken).ConfigureAwait(false);
        return
        [
            CheckProfileLayout(profile),
            manifest,
            CheckModFolder(profile, listedMods),
            CheckSessionLog(profile),
        ];
    }

    private ProfilePaths SharedProfile()
    {
        var root = _paths.GetSharedProfileRoot();
        return new ProfilePaths(
            root,
            Path.Combine(root, "mods"),
            Path.Combine(root, "manifest.toml"),
            Path.Combine(root, "logs", "KittenSpaceAgency.log"),
            GameCreated: true);
    }

    private ProfilePaths Instance(Guid instanceId) => new(
        _paths.GetInstanceRoot(instanceId),
        _paths.GetInstanceModsFolder(instanceId),
        _paths.GetInstanceManifestPath(instanceId),
        _paths.GetInstanceGameLogPath(instanceId),
        GameCreated: false);

    private static GameAssumptionResult CheckGameAssembly(string? gameDirectory, InstalledGameVersion? version)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return NotChecked(GameAssumption.GameAssembly, "No game directory is set.");

        // A directory without the assembly is not an installation, so there is
        // nothing to say about where the build is kept.
        var assembly = Path.Combine(gameDirectory, "KSA.dll");
        if (!File.Exists(assembly))
            return NotChecked(GameAssumption.GameAssembly, $"{assembly} is not there.");

        return version is null
            ? Broken(GameAssumption.GameAssembly, $"{assembly} carries no version Borea can read.")
            : Holds(GameAssumption.GameAssembly, $"KSA.dll reports {version.RawVersion}.");
    }

    private static async Task<GameAssumptionResult> CheckContentManifestAsync(string? gameDirectory, CancellationToken cancellationToken)
    {
        if (Content(gameDirectory) is not { } content)
            return NotChecked(GameAssumption.ContentManifest, "No game content was found to check.");

        var path = Path.Combine(content, "manifest.toml");
        if (!File.Exists(path))
            return Broken(GameAssumption.ContentManifest, $"{path} is missing.");

        ManifestDto? manifest;
        try
        {
            manifest = await TomlFileStore.ReadAsync<ManifestDto>(path, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return Broken(GameAssumption.ContentManifest, $"{path} did not read as a manifest. {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotChecked(GameAssumption.ContentManifest, $"{path} could not be read. {exception.Message}");
        }

        return manifest is not null && manifest.Mods.Any(entry => !string.IsNullOrWhiteSpace(entry.Id))
            ? Holds(GameAssumption.ContentManifest, $"{path} lists {Entries(manifest.Mods.Count)}.")
            : Broken(GameAssumption.ContentManifest, $"{path} lists no [[mods]] entry with an id.");
    }

    private static GameAssumptionResult CheckPatchNotes(string? gameDirectory)
    {
        if (Content(gameDirectory) is not { } content)
            return NotChecked(GameAssumption.PatchNotes, "No game content was found to check.");

        var folder = Path.Combine(content, "Versions");
        if (!Directory.Exists(folder))
            return Broken(GameAssumption.PatchNotes, $"{folder} is missing.");

        string[] files;
        try
        {
            files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotChecked(GameAssumption.PatchNotes, $"{folder} could not be read. {exception.Message}");
        }

        if (files.Length == 0)
            return Broken(GameAssumption.PatchNotes, $"{folder} holds no JSON file.");

        foreach (var file in files)
        {
            try
            {
                if (GamePatchNotesFile.Parse(File.ReadAllBytes(file)) is not null)
                    return Holds(GameAssumption.PatchNotes, $"{folder} holds {files.Length} patch notes files.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return NotChecked(GameAssumption.PatchNotes, $"{file} could not be read. {exception.Message}");
            }
        }

        return Broken(GameAssumption.PatchNotes, $"No file in {folder} is in the format Borea reads.");
    }

    /// <summary>
    /// The Content folder of the installation, or null when there is none. Its
    /// absence means the directory is not an installation, not that the game
    /// stopped keeping its content there.
    /// </summary>
    private static string Entries(int count) => count == 1 ? "1 entry" : $"{count} entries";

    private static string? Content(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return null;

        var content = Path.Combine(gameDirectory, "Content");
        return Directory.Exists(content) ? content : null;
    }

    private static GameAssumptionResult CheckProfileLayout(ProfilePaths profile)
    {
        // An instance holds Borea's own files next to the game's, and its layout
        // is Borea's doing, so only the profile the game creates is evidence.
        if (!profile.GameCreated)
            return NotChecked(GameAssumption.ProfileLayout, "Borea creates this profile, so the profile of the game is the one checked.");

        if (!Directory.Exists(profile.Root))
            return NotChecked(GameAssumption.ProfileLayout, $"{profile.Root} does not exist yet.");

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(profile.Root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotChecked(GameAssumption.ProfileLayout, $"{profile.Root} could not be read. {exception.Message}");
        }

        if (entries.Length == 0)
            return NotChecked(GameAssumption.ProfileLayout, $"{profile.Root} is empty.");

        var names = entries.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Overlaps(ProfileFolderNames) || names.Contains("manifest.toml")
            ? Holds(GameAssumption.ProfileLayout, $"{profile.Root} holds the folders the game writes.")
            : Broken(GameAssumption.ProfileLayout, $"{profile.Root} holds none of the names the game uses: {string.Join(", ", ProfileFolderNames)}.");
    }

    /// <param name="ListedMods">The ids the manifest names, which is what the profile is supposed to hold.</param>
    private static async Task<(GameAssumptionResult Result, IReadOnlyList<string> ListedMods)> CheckProfileManifestAsync(ProfilePaths profile, CancellationToken cancellationToken)
    {
        if (!File.Exists(profile.Manifest))
            return (NotChecked(GameAssumption.ProfileManifest, $"{profile.Manifest} does not exist yet."), []);

        ManifestDto? manifest;
        try
        {
            // A manifest without mods serializes to nothing, so an empty file is not a broken one.
            if (string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(profile.Manifest, cancellationToken).ConfigureAwait(false)))
                return (NotChecked(GameAssumption.ProfileManifest, $"{profile.Manifest} is empty."), []);

            manifest = await TomlFileStore.ReadAsync<ManifestDto>(profile.Manifest, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return (Broken(GameAssumption.ProfileManifest, $"{profile.Manifest} did not read as a manifest. {exception.Message}"), []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (NotChecked(GameAssumption.ProfileManifest, $"{profile.Manifest} could not be read. {exception.Message}"), []);
        }

        if (manifest is not { Mods.Count: > 0 })
            return (Broken(GameAssumption.ProfileManifest, $"{profile.Manifest} has content but lists no mods entry."), []);

        var listed = manifest.Mods
            .Select(entry => entry.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        return (Holds(GameAssumption.ProfileManifest, $"{profile.Manifest} lists {Entries(manifest.Mods.Count)}."), listed);
    }

    /// <summary>
    /// Only the folders the manifest names are evidence about the game. A folder
    /// nobody listed is one the user put there, such as an archive unpacked by
    /// hand or a mod that is still being written, and it says nothing about the
    /// shape the game gives a mod.
    /// </summary>
    private static GameAssumptionResult CheckModFolder(ProfilePaths profile, IReadOnlyList<string> listedMods)
    {
        if (!Directory.Exists(profile.Mods))
            return NotChecked(GameAssumption.ModFolder, $"{profile.Mods} does not exist yet.");

        if (listedMods.Count == 0)
            return NotChecked(GameAssumption.ModFolder, $"The manifest names no mod that {profile.Mods} is supposed to hold.");

        string[] folders;
        try
        {
            folders = Directory.GetDirectories(profile.Mods);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotChecked(GameAssumption.ModFolder, $"{profile.Mods} could not be read. {exception.Message}");
        }

        var listedFolders = folders
            .Where(folder => listedMods.Any(id => ModIds.Equals(Path.GetFileName(folder), id)))
            .ToList();

        // A listed mod without a folder is a mod that was removed, not a changed shape.
        if (listedFolders.Count == 0)
            return NotChecked(GameAssumption.ModFolder, $"No mod the manifest names has a folder in {profile.Mods}.");

        return listedFolders.Any(folder => File.Exists(Path.Combine(folder, ModFolders.DefinitionFileName)))
            ? Holds(GameAssumption.ModFolder, $"{profile.Mods} holds mods with a {ModFolders.DefinitionFileName}.")
            : Broken(GameAssumption.ModFolder, $"No mod the manifest names holds a {ModFolders.DefinitionFileName} in {profile.Mods}.");
    }

    private static GameAssumptionResult CheckSessionLog(ProfilePaths profile)
    {
        var folder = Path.GetDirectoryName(profile.GameLog)!;
        if (!Directory.Exists(folder))
            return NotChecked(GameAssumption.SessionLog, $"{folder} does not exist yet.");

        string[] logs;
        try
        {
            logs = Directory.GetFiles(folder, "*.log");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotChecked(GameAssumption.SessionLog, $"{folder} could not be read. {exception.Message}");
        }

        // Borea writes its own launch log next to the logs of the game, and the
        // game leaves the tail of a recovered crash there, which is no session.
        // Neither says anything about how the game names a session.
        var ownName = Path.GetFileName(profile.LaunchLog);
        var gameLogs = logs.Count(path =>
            !string.Equals(Path.GetFileName(path), ownName, StringComparison.OrdinalIgnoreCase)
            && !GameLogFiles.IsKnownNonSession(Path.GetFileName(path)));
        if (gameLogs == 0)
            return NotChecked(GameAssumption.SessionLog, $"{folder} holds no game log yet.");

        var known = GameLogFiles.Find(profile.GameLog).Count;
        return known > 0
            ? Holds(GameAssumption.SessionLog, $"{folder} holds {known} logs named the way the game names them.")
            : Broken(GameAssumption.SessionLog, $"{folder} holds {gameLogs} logs and none of them carries a name Borea knows.");
    }

    private static GameAssumptionResult Holds(GameAssumption assumption, string detail)
        => new(assumption, GameAssumptionState.Holds, detail);

    private static GameAssumptionResult Broken(GameAssumption assumption, string detail)
        => new(assumption, GameAssumptionState.Broken, detail);

    private static GameAssumptionResult NotChecked(GameAssumption assumption, string detail)
        => new(assumption, GameAssumptionState.NotChecked, detail);

    /// <param name="GameLog">The undated log of the game, which also names the folder its other logs are in.</param>
    /// <param name="GameCreated">Whether the game created this profile, rather than Borea.</param>
    private sealed record ProfilePaths(string Root, string Mods, string Manifest, string GameLog, bool GameCreated)
    {
        public string LaunchLog => Path.Combine(Path.GetDirectoryName(GameLog)!, "borea-launch.log");
    }
}
