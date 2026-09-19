using Borea.Core.Launch;
using Borea.Core.Paths;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Toml;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Launch;

/// <summary>
/// The enabled mods of an instance manifest in load order, each with whether
/// the loader would load code from it. What cannot be read counts as no code.
/// </summary>
internal static class LoadOrderReader
{
    public static async Task<IReadOnlyList<LoadOrderMod>> ReadAsync(IGamePathProvider paths, Guid instanceId, CancellationToken cancellationToken)
    {
        ManifestDto? manifest;
        try
        {
            manifest = await TomlFileStore.ReadAsync<ManifestDto>(paths.GetInstanceManifestPath(instanceId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var modsFolder = paths.GetInstanceModsFolder(instanceId);
        return (manifest?.Mods ?? [])
            .Where(entry => entry.Enabled && !string.IsNullOrWhiteSpace(entry.Id))
            .Select(entry => Read(modsFolder, entry.Id))
            .ToList();
    }

    /// <summary>
    /// Reads the mod folder the way RuntimeMod.TryCreateMod does. The entry
    /// assembly is [StarMap].EntryAssembly, or the mod id without that table.
    /// </summary>
    private static LoadOrderMod Read(string modsFolder, string modId)
    {
        string? folder;
        string definition;
        try
        {
            folder = ModFolders.Find(modsFolder, modId);
            var path = folder is null ? null : Path.Combine(folder, ModFolders.DefinitionFileName);
            if (path is null || !File.Exists(path))
                return new LoadOrderMod(modId, IsCodeMod: false);

            definition = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new LoadOrderMod(modId, IsCodeMod: false);
        }

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(definition) ?? new TomlTable();
        }
        catch (TomlException)
        {
            return new LoadOrderMod(modId, IsCodeMod: false) { HasInvalidDefinition = true };
        }

        string? entryAssembly = modId;
        var optional = new List<string>();
        if (root.TryGetValue("StarMap", out var starMap))
        {
            var table = starMap as TomlTable;
            entryAssembly = table is not null && table.TryGetValue("EntryAssembly", out var value) ? value as string : null;
            if (table is not null && table.TryGetValue("ModDependencies", out var dependencies))
                optional.AddRange(OptionalDependencies(dependencies));
        }

        var isCodeMod = !string.IsNullOrWhiteSpace(entryAssembly) && File.Exists(Path.Combine(folder!, entryAssembly + ".dll"));
        return new LoadOrderMod(modId, isCodeMod) { OptionalDependencies = optional };
    }

    private static IEnumerable<string> OptionalDependencies(object? dependencies)
    {
        IEnumerable<object?> entries = dependencies switch
        {
            TomlTableArray tables => tables,
            TomlArray array => array,
            _ => [],
        };

        return entries
            .OfType<TomlTable>()
            .Where(entry => entry.TryGetValue("Optional", out var optional) && optional is true)
            .Select(entry => entry.TryGetValue("ModId", out var id) ? id as string : null)
            .OfType<string>();
    }
}
