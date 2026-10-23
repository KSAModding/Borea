using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Mods;

namespace Borea.Storage.Launch;

internal static class ModBlame
{
    private static readonly EnumerationOptions AllFiles = new() { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };

    /// <summary>
    /// The installed mod behind the first of the assemblies: a mod whose id is
    /// the assembly's name, or whose folder holds that assembly.
    /// </summary>
    public static InstalledMod? Find(Instance instance, string modsFolder, IEnumerable<string> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var byId = instance.Mods.FirstOrDefault(mod => string.Equals(mod.ModId, assembly, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;

            var byFolder = instance.Mods.FirstOrDefault(mod => HoldsAssembly(modsFolder, mod.ModId, assembly));
            if (byFolder is not null)
                return byFolder;
        }

        return null;
    }

    private static bool HoldsAssembly(string modsFolder, string modId, string assembly)
    {
        try
        {
            // the folder can differ from the id in case, which a case-sensitive file system does not match by path
            return ModFolders.Find(modsFolder, modId) is { } folder && Directory.EnumerateFiles(folder, assembly + ".dll", AllFiles).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // a folder that cannot be searched, such as a link to a removed folder, blames nothing
            return false;
        }
    }
}
