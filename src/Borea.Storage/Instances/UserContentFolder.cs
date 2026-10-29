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
    public static void Check(string folder, ModVersionMetadata release) =>
        Check(release, name => HasFile(folder, name), name => File.OpenRead(Path.Combine(folder, name)));

    /// <summary>
    /// The same check over files that are not in a folder, such as the entries of an archive that the preview reads
    /// in place. <paramref name="hasFile"/> tells whether the root holds a file of that name, and
    /// <paramref name="open"/> opens it for reading, or gives null when it cannot.
    /// </summary>
    /// <exception cref="InvalidOperationException">The files are not ones the game reads as this release.</exception>
    public static void Check(ModVersionMetadata release, Func<string, bool> hasFile, Func<string, Stream?> open)
    {
        var what = $"The archive of '{release.ModId}' {release.Version}";
        if (!hasFile(MetadataFileName))
            throw new InvalidOperationException($"{what} has no {MetadataFileName} at its root, so the game would skip it.");

        string text;
        using (var stream = open(MetadataFileName) ?? throw new InvalidOperationException($"{what} has a {MetadataFileName} that Borea cannot read."))
        using (var textReader = new StreamReader(stream))
            text = textReader.ReadToEnd();

        if (!FileGameSaveStore.TryReadName(text, out var name))
            throw new InvalidOperationException($"{what} has a {MetadataFileName} that is not TOML.");

        if (!string.Equals(name, release.Install?.Folder, StringComparison.Ordinal))
            throw new InvalidOperationException($"{what} has the name '{name}' in its {MetadataFileName}, and its release names the folder '{release.Install?.Folder}'.");

        var gameFile = GameFileName(release.Type);
        if (!hasFile(gameFile))
            throw new InvalidOperationException($"{what} has no {gameFile} at its root, so the game would skip it.");

        try
        {
            using var stream = open(gameFile) ?? throw new InvalidOperationException($"{what} has a {gameFile} that Borea cannot read.");
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
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
