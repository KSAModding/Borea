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

    /// <summary>
    /// Copies of EXECUTABLE_EXTENSIONS and BUNDLE_EXTENSIONS in tools/stamp_release.py of content-index-releases, and a change
    /// there needs the same change here. The game reads only meta.toml and XML from these folders and runs nothing from them.
    /// </summary>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".cpl", ".dll", ".xll", ".msi", ".msp",
        ".msix", ".msixbundle", ".appx", ".appxbundle", ".application", ".appref-ms",
        ".diagcab", ".gadget",
        ".bat", ".cmd", ".ps1", ".psm1", ".vb", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".wsc", ".sct", ".hta", ".msc", ".reg", ".lnk", ".url",
        ".chm", ".settingcontent-ms",
        ".scf", ".library-ms", ".searchconnector-ms",
        ".jar", ".jnlp", ".py", ".pyc", ".pyw", ".pyz",
        ".so", ".sh", ".bash", ".zsh", ".ksh", ".csh", ".run", ".appimage", ".desktop",
        ".deb", ".rpm",
        ".dylib", ".app", ".command", ".tool", ".terminal", ".pkg", ".mpkg",
        ".webloc", ".inetloc", ".fileloc",
    };

    /// <summary>The types that macOS also runs as a folder.</summary>
    private static readonly HashSet<string> BundleExtensions = new(StringComparer.OrdinalIgnoreCase) { ".app", ".pkg", ".mpkg" };

    /// <summary>GAME_FILE_LIMIT of the same file, for each XML file and for all of them together.</summary>
    private const long XmlLimit = 256L * 1024 * 1024;

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

    /// <summary>
    /// Refuses a file or folder of a type that runs as code, at any depth, and an XML file that is too large, not well-formed
    /// or carries a DOCTYPE, as the index refuses them in the archive (RFC 0085). Borea installs from the archive and checks
    /// again. <see cref="Check(string, ModVersionMetadata)"/> parses the game file itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">The folder holds such a file.</exception>
    public static void CheckFiles(string folder, ModVersionMetadata release)
    {
        var what = $"The archive of '{release.ModId}' {release.Version}";
        var gameFile = GameFileName(release.Type);
        var xmlFiles = new List<(string Path, string FullName)>();
        long total = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", InstalledContentHashes.EveryFile))
        {
            var parts = Path.GetRelativePath(folder, file.FullName).Split(Path.DirectorySeparatorChar);

            // Windows drops dots and spaces at the end of a name when it creates the file, so Setup.exe. lands as Setup.exe.
            var names = Array.ConvertAll(parts, part => part.TrimEnd('.', ' '));
            for (var depth = 1; depth <= names.Length; depth++)
            {
                var isFile = depth == names.Length;
                var extension = Path.GetExtension(names[depth - 1]);
                if ((isFile ? ExecutableExtensions : BundleExtensions).Contains(extension))
                    throw new InvalidOperationException($"{what} holds '{string.Join('/', parts[..depth])}', a {extension.ToLowerInvariant()} {(isFile ? "file" : "folder")}. That type runs as code, and the game runs nothing from a vehicle or save folder.");
            }

            if (!names[^1].EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = string.Join('/', parts);
            if (file.Length > XmlLimit)
                throw new InvalidOperationException($"{what} has '{path}' of {file.Length} bytes, above the limit of {XmlLimit} bytes for an XML file.");

            total += file.Length;
            if (!string.Equals(path, gameFile, StringComparison.Ordinal))
                xmlFiles.Add((path, file.FullName));
        }

        if (total > XmlLimit)
            throw new InvalidOperationException($"{what} has XML files of {total} bytes together, above the limit of {XmlLimit} bytes.");

        foreach (var (path, fullName) in xmlFiles)
        {
            try
            {
                // XmlReader reads a path string as a URI, which would open stateA.xml for the file state%41.xml.
                using var stream = File.OpenRead(fullName);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                while (reader.Read())
                {
                }
            }
            catch (XmlException exception)
            {
                throw new InvalidOperationException($"{what} has '{path}', which is not well-formed XML or carries a DOCTYPE. {exception.Message}", exception);
            }
        }
    }

    /// <summary>
    /// Whether the folder has a file of exactly this name. The game looks the file up by its exact name, which a file system
    /// that ignores case would hide, and the release checks compare the names in the archive exactly.
    /// </summary>
    public static bool HasFile(string folder, string name) =>
        Directory.Exists(folder) && new DirectoryInfo(folder).EnumerateFiles().Any(file => string.Equals(file.Name, name, StringComparison.Ordinal));
}
