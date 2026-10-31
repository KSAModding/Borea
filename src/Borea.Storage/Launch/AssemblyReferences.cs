using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Storage.Mods;

namespace Borea.Storage.Launch;

/// <summary>
/// Reads which assemblies the DLLs in a folder reference, from their metadata
/// and without loading them. When the output of a loader is cut short, the name
/// of the assembly that could not load still tells who needed it.
/// </summary>
internal static class AssemblyReferences
{
    private static readonly EnumerationOptions AllFiles = new() { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };
    private static readonly EnumerationOptions TopFiles = new() { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };

    /// <summary>
    /// Whether a .NET DLL in the folder, or with <paramref name="recurse"/> also
    /// in its subfolders, references one of the assemblies. An assembly that the
    /// folder holds itself does not count, because StarMap resolves a reference
    /// in the folder of the assembly that makes it.
    /// </summary>
    public static bool Referencing(string folder, IReadOnlyCollection<string> names, bool recurse = true)
    {
        if (names.Count == 0)
            return false;

        var wanted = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Needs(folder, recurse, wanted.Contains);
    }

    /// <summary>
    /// The enabled code mods in load order whose folder references an assembly
    /// that <paramref name="missing"/> accepts and that the folder does not hold.
    /// </summary>
    public static IReadOnlyList<InstalledMod> Mods(Instance instance, string modsFolder, IReadOnlyList<LoadOrderMod> loadOrder, Func<string, bool> missing)
    {
        return loadOrder
            .Where(entry => entry.IsCodeMod)
            .Select(entry => instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, entry.ModId)))
            .OfType<InstalledMod>()
            .Where(mod => Folder(modsFolder, mod.ModId) is { } folder && Needs(folder, recurse: true, missing))
            .ToList();
    }

    /// <summary>The names of the DLLs in the folder, or with <paramref name="recurse"/> also in its subfolders, without ".dll".</summary>
    public static HashSet<string> Held(string folder, bool recurse) =>
        Dlls(folder, recurse).Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool Needs(string folder, bool recurse, Func<string, bool> missing)
    {
        var files = Dlls(folder, recurse);
        var held = files.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return files.Any(file => References(file).Any(name => !held.Contains(name) && missing(name)));
    }

    private static string[] Dlls(string folder, bool recurse)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return [];

        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.dll", recurse ? AllFiles : TopFiles) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static string? Folder(string modsFolder, string modId)
    {
        try
        {
            return ModFolders.Find(modsFolder, modId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> References(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var image = new PEReader(stream);
            if (!image.HasMetadata)
                return [];

            var metadata = image.GetMetadataReader();
            return metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToList();
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // a native DLL or a file that is not a DLL references nothing
            return [];
        }
    }
}
