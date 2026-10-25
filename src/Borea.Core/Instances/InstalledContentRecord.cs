using System.Collections.ObjectModel;
using Borea.Core.Mods;

namespace Borea.Core.Instances;

/// <summary>
/// A vehicle or save that Borea installed (RFC 0085). A folder without a record is the player's own,
/// and Borea never replaces or removes it.
/// </summary>
public sealed class InstalledContentRecord
{
    public ContentType Type => Metadata.Type;

    public string ListingId => Metadata.ModId;

    public ModVersion Version => Metadata.Version;

    /// <summary>The stamped release it was installed from, as <see cref="InstalledMod.Metadata"/> keeps it.</summary>
    public ModVersionMetadata Metadata { get; }

    /// <summary>The folder under Vehicles or saves, which the player may have renamed since.</summary>
    public string FolderName { get; }

    public DateTimeOffset InstalledAt { get; }

    /// <summary>
    /// The lowercase hex SHA-256 of each file after any rename at install, by its path in the folder
    /// with "/" between the parts. The keys compare ordinally.
    /// </summary>
    public IReadOnlyDictionary<string, string> Files { get; }

    public InstalledContentRecord(
        ModVersionMetadata metadata,
        string folderName,
        DateTimeOffset installedAt,
        IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(files);

        if (!metadata.Type.IsVehicleOrSave())
            throw new ArgumentException("Only a vehicle or save has an install record of this kind.", nameof(metadata));

        if (!GameSaveName.IsValid(folderName))
            throw new ArgumentException($"'{folderName}' is not a folder name the game keeps.", nameof(folderName));

        foreach (var (path, hash) in files)
        {
            if (!IsRelativeFilePath(path))
                throw new ArgumentException($"'{path}' is not a relative file path with '/' between its parts.", nameof(files));

            if (hash is not { Length: 64 } || !hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
                throw new ArgumentException($"The hash of '{path}' is not a lowercase hex SHA-256.", nameof(files));
        }

        Metadata = metadata;
        FolderName = folderName;
        InstalledAt = installedAt;
        Files = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(files, StringComparer.Ordinal));
    }

    public InstalledContentRecord WithFolderName(string folderName) => new(Metadata, folderName, InstalledAt, Files);

    private static bool IsRelativeFilePath(string path) =>
        path.Length > 0
        && !path.Contains('\\', StringComparison.Ordinal)
        && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
}

/// <summary>What changed in a recorded folder since the install. Each list is sorted ordinally.</summary>
public sealed record InstalledContentChanges(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed)
{
    public bool IsUnchanged => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}
