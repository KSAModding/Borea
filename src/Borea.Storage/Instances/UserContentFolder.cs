using System.Xml;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Instances;

/// <summary>
/// The folder of one vehicle or save, as the game reads it: UncompressedVehicleSave.FromDirectory and
/// UncompressedSave.FromDirectory skip a folder without meta.toml (SaveMetaData.FromDirectory), and
/// one without vehicle.xml (VehicleSaveData.ExistsIn) or universe.xml (UniverseData.ExistsIn).
/// </summary>
internal static class UserContentFolder
{
    public const string MetadataFileName = "meta.toml";

    public static string GameFileName(ContentType type) => type == ContentType.Save ? "universe.xml" : "vehicle.xml";

    public static GameSaveKind Kind(ContentType type) => type == ContentType.Save ? GameSaveKind.Save : GameSaveKind.Vehicle;

    /// <summary>The Vehicles or saves folder of the instance, in the letter case found on disk.</summary>
    public static string KindFolder(IGamePathProvider paths, Guid instanceId, ContentType type) =>
        FileGameSaveStore.FindFolder(type == ContentType.Save ? paths.GetInstanceSavesFolder(instanceId) : paths.GetInstanceVehiclesFolder(instanceId));

    /// <summary>
    /// Whether another folder in the kind folder has the name, or a meta.toml with that name, ignoring case,
    /// because the game shows two items of one name as one. The folder <paramref name="own"/> does not count.
    /// </summary>
    public static bool IsTaken(string kindFolder, string name, string? own = null)
    {
        var directory = new DirectoryInfo(kindFolder);
        if (!directory.Exists)
            return false;

        foreach (var folder in directory.EnumerateDirectories())
        {
            if (own is not null && string.Equals(folder.Name, own, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(FileGameSaveStore.ReadMetadata(Path.Combine(folder.FullName, MetadataFileName)).Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks an unpacked folder the way the game reads it. Borea parses meta.toml with Tomlyn and the game
    /// with Tomlet, so a file that passes can still fail in the game.
    /// </summary>
    /// <exception cref="InvalidOperationException">The folder is not one the game reads as this release.</exception>
    public static void Check(string folder, ModVersionMetadata release)
    {
        var what = $"The archive of '{release.ModId}' {release.Version}";
        var metadata = Path.Combine(folder, MetadataFileName);
        if (!HasFile(folder, MetadataFileName))
            throw new InvalidOperationException($"{what} has no {MetadataFileName} at its root, so the game would skip it.");

        if (!FileGameSaveStore.TryReadName(File.ReadAllText(metadata), out var name))
            throw new InvalidOperationException($"{what} has a {MetadataFileName} that is not TOML.");

        if (!string.Equals(name, release.Install?.Folder, StringComparison.Ordinal))
            throw new InvalidOperationException($"{what} has the name '{name}' in its {MetadataFileName}, and its release names the folder '{release.Install?.Folder}'.");

        var gameFile = GameFileName(release.Type);
        if (!HasFile(folder, gameFile))
            throw new InvalidOperationException($"{what} has no {gameFile} at its root, so the game would skip it.");

        try
        {
            using var reader = XmlReader.Create(Path.Combine(folder, gameFile), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
            }
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException($"{what} has a {gameFile} that is not well-formed XML. {exception.Message}", exception);
        }
    }

    // The game looks the file up by its exact name, which a file system that ignores case would hide.
    private static bool HasFile(string folder, string name) =>
        new DirectoryInfo(folder).EnumerateFiles().Any(file => string.Equals(file.Name, name, StringComparison.Ordinal));
}
