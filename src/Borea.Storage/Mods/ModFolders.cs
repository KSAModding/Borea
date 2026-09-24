using Borea.Core.Mods;

namespace Borea.Storage.Mods;

/// <summary>
/// Finds a mod's folder in an instance's mods folder. Resolved by comparison
/// and not by path, because ids ignore case and Linux paths do not.
/// </summary>
internal static class ModFolders
{
    /// <summary>The file that makes a folder a mod to the game (ModLibrary.AddMods).</summary>
    public const string DefinitionFileName = "mod.toml";
    public const string OwnershipFileName = ".borea-owner";

    /// <summary>
    /// The folder under <paramref name="modsFolder"/> that carries the id, or
    /// null when there is none.
    /// </summary>
    public static string? Find(string modsFolder, string modId)
    {
        if (!Directory.Exists(modsFolder))
            return null;

        return Directory.EnumerateDirectories(modsFolder)
            .FirstOrDefault(d => ModIds.Equals(Path.GetFileName(d), modId));
    }

    /// <summary>
    /// The folder Borea installed for <paramref name="installed"/>, proved by
    /// the ownership marker of a private folder or by the link of a linked one.
    /// Null when there is none, or more than one.
    /// </summary>
    public static string? FindOwned(string modsFolder, InstalledMod installed, ModStore store)
    {
        if (installed.Storage == ModStorage.Private)
            return installed.OwnershipToken is null ? null : FindOwned(modsFolder, installed.ModId, installed.OwnershipToken);

        if (!Directory.Exists(modsFolder))
            return null;

        var entry = store.EntryPath(installed);
        var linked = Directory.EnumerateDirectories(modsFolder)
            .Where(path => ModIds.Equals(Path.GetFileName(path), installed.ModId) && store.IsLinkTo(path, entry))
            .Take(2)
            .ToList();
        return linked.Count == 1 ? linked[0] : null;
    }

    public static string? FindOwned(string modsFolder, string modId, string ownershipToken)
    {
        if (!Directory.Exists(modsFolder))
            return null;

        string? ownedFolder = null;
        foreach (var directory in Directory.EnumerateDirectories(modsFolder)
            .Where(path => ModIds.Equals(Path.GetFileName(path), modId)))
        {
            try
            {
                var markerPath = Path.Combine(directory, OwnershipFileName);
                if (!File.Exists(markerPath)
                    || !string.Equals(File.ReadAllText(markerPath), ownershipToken, StringComparison.Ordinal))
                {
                    continue;
                }

                if (ownedFolder is not null)
                    return null;

                ownedFolder = directory;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        return ownedFolder;
    }

    /// <summary>
    /// Copies a mod folder without the ownership marker at its root, because a
    /// marker from elsewhere would claim that Borea installed the copy.
    /// </summary>
    public static void CopyWithoutMarker(string source, string target, CancellationToken cancellationToken)
        => Copy(source, target, isModRoot: true, cancellationToken);

    private static void Copy(string source, string target, bool isModRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            if (!isModRoot || !string.Equals(fileName, OwnershipFileName, StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(target, fileName));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
            Copy(directory, Path.Combine(target, Path.GetFileName(directory)), isModRoot: false, cancellationToken);
    }
}
