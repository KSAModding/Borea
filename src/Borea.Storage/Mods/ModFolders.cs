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
}
