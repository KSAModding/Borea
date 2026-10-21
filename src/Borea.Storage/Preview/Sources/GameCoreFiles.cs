using Borea.Storage.State;
using Tomlyn;

namespace Borea.Storage.Preview.Sources;

/// <summary>Whether the game folder can give the stock parts to a preview.</summary>
internal enum GameCoreStatus
{
    Ok,

    /// <summary>No game folder is set, or the folder does not exist.</summary>
    NoGame,

    /// <summary>The folder exists but holds no Content/Core, so it is not a KSA install.</summary>
    GameInvalid,
}

/// <summary>
/// The mods that ship in the Content folder of the game, in the order of Content/manifest.toml, each as a
/// <see cref="FolderPreviewFiles"/> over Content/&lt;id&gt;.
/// </summary>
/// <remarks>
/// ModLibrary.PrepareManifest reads Content/manifest.toml and marks every mod it lists as Core, and
/// ModLibrary.PrepareAll then loads each enabled entry from Content/&lt;id&gt; when that folder holds a mod.toml. In the
/// stock install the manifest lists only Core. The player's own manifest can disable a content mod, which the preview
/// does not read. A manifest that cannot be read gives Core alone, because Content/Core is what makes the folder an
/// install. An entry with enabled = false, an id that is not one plain folder name, or a folder without a mod.toml is
/// left out.
/// </remarks>
internal sealed class GameCoreFiles
{
    public const string CoreId = "Core";

    private const long MaxManifestBytes = 1024 * 1024;

    private GameCoreFiles(GameCoreStatus status, IReadOnlyList<KeyValuePair<string, FolderPreviewFiles>> mods)
    {
        Status = status;
        Mods = mods;
    }

    public GameCoreStatus Status { get; }

    /// <summary>The content mods in load order, keyed by their manifest id. Empty unless <see cref="Status"/> is Ok.</summary>
    public IReadOnlyList<KeyValuePair<string, FolderPreviewFiles>> Mods { get; }

    public static GameCoreFiles Open(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return new GameCoreFiles(GameCoreStatus.NoGame, []);

        var content = Path.Combine(gameDirectory, "Content");
        if (!Directory.Exists(Path.Combine(content, CoreId)))
            return new GameCoreFiles(GameCoreStatus.GameInvalid, []);

        var ids = ReadManifestIds(Path.Combine(content, "manifest.toml")) ?? [CoreId];
        var mods = new List<KeyValuePair<string, FolderPreviewFiles>>();
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (PreviewPath.Normalize(id) is not { } name || name.Contains('/', StringComparison.Ordinal))
                continue;

            var folder = Path.Combine(content, name);
            if (File.Exists(Path.Combine(folder, "mod.toml")))
                mods.Add(new(id, new FolderPreviewFiles(folder)));
        }

        return new GameCoreFiles(GameCoreStatus.Ok, mods);
    }

    private static IReadOnlyList<string>? ReadManifestIds(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxManifestBytes)
                return null;

            var manifest = TomlSerializer.Deserialize<ManifestDto>(File.ReadAllText(path));
            var ids = manifest?.Mods
                .Where(entry => entry.Enabled && !string.IsNullOrWhiteSpace(entry.Id))
                .Select(entry => entry.Id.Trim())
                .ToList();
            return ids is { Count: > 0 } ? ids : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TomlException)
        {
            return null;
        }
    }
}
