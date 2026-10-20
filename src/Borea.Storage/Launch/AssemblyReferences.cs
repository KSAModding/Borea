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

    /// <summary>
    /// Whether a .NET DLL under the folder references one of the assemblies. An
    /// assembly that the folder holds itself does not count, because StarMap
    /// resolves a reference in the folder of the assembly that makes it.
    /// </summary>
    public static bool Referencing(string folder, IReadOnlyCollection<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0 || string.IsNullOrWhiteSpace(folder))
            return false;

        string[] files;
        try
        {
            if (!Directory.Exists(folder))
                return false;
            files = Directory.GetFiles(folder, "*.dll", AllFiles);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        var held = files.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = names.Where(name => !held.Contains(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return missing.Count > 0 && files.Any(file => References(file).Any(missing.Contains));
    }

    /// <summary>
    /// The enabled code mods in load order whose folder references one of the
    /// assemblies. StarMap stops at the first of them, and the others would stop
    /// it next.
    /// </summary>
    public static IReadOnlyList<InstalledMod> Mods(Instance instance, string modsFolder, IReadOnlyList<LoadOrderMod> loadOrder, IReadOnlyCollection<string> names)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(loadOrder);
        if (names.Count == 0)
            return [];

        return loadOrder
            .Where(entry => entry.IsCodeMod)
            .Select(entry => instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, entry.ModId)))
            .OfType<InstalledMod>()
            .Where(mod => Folder(modsFolder, mod.ModId) is { } folder && Referencing(folder, names))
            .ToList();
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
