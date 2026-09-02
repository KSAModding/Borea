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
}
