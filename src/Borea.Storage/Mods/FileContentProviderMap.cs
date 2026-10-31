using System.Xml;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Sharing;
using Borea.Storage.State;
using Borea.Storage.Toml;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Mods;

/// <summary>
/// Finds each mod folder the way ModLibrary.PrepareAll does: the entries of the instance manifest in order, each in the game's
/// Content folder first and then in the instance's mods folder. The game's own content (Content/manifest.toml) that the manifest
/// lacks follows, as ModLibrary.PrepareManifest appends it, and the folders of mods that the manifest does not name come last,
/// as disabled.
/// </summary>
public sealed class FileContentProviderMap : IContentProviderMap
{
    /// <summary>The element names of AssetBundle and SystemTemplate that define a celestial body.</summary>
    private static readonly HashSet<string> BodyElements = new(StringComparer.Ordinal)
    {
        "Comet", "InterstellarComet", "PeriodicComet", "AtmosphericBody", "StellarBody", "Barycenter", "PlanetaryBody", "MinorBody", "Asteroid", "TerrestrialBody",
    };

    /// <summary>The phases SubstanceTemplate.Create makes, one per child element present.</summary>
    private static readonly Dictionary<string, string> Phases = new(StringComparer.Ordinal) { ["Solid"] = "(s)", ["Liquid"] = "(l)", ["Gas"] = "(g)" };

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    private readonly IGamePathProvider _paths;

    public FileContentProviderMap(IGamePathProvider paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public Task<ContentProviders> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadCoreAsync(instanceId, cancellationToken), cancellationToken);

    private async Task<ContentProviders> ReadCoreAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var unreadable = new List<string>();
        var contentFolder = _paths.GetGameDirectoryPath() is { } game ? Path.Combine(game, "Content") : null;
        var gameContent = contentFolder is null ? [] : await ReadManifestAsync(Path.Combine(contentFolder, "manifest.toml"), unreadable, cancellationToken).ConfigureAwait(false);
        var entries = await ReadManifestAsync(_paths.GetInstanceManifestPath(instanceId), unreadable, cancellationToken).ConfigureAwait(false);

        var modsFolder = _paths.GetInstanceModsFolder(instanceId);
        var named = new HashSet<string>(ModIds.Comparer);
        var providers = new List<ContentProvider>();
        foreach (var entry in entries.Concat(gameContent.Where(content => !entries.Any(entry => string.Equals(entry.Id, content.Id, StringComparison.Ordinal)))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Id) || !named.Add(entry.Id))
                continue;

            var folder = WithDefinition(contentFolder, entry.Id) ?? WithDefinition(modsFolder, entry.Id);
            if (folder is not null)
                providers.Add(Read(folder, entry.Id, entry.Enabled, gameContent.Any(content => ModIds.Equals(content.Id, entry.Id)), unreadable));
        }

        var others = Directory.Exists(modsFolder) ? Directory.EnumerateDirectories(modsFolder).Select(Path.GetFileName).OfType<string>() : [];
        foreach (var name in others.Where(name => !named.Contains(name)).Order(StringComparer.Ordinal))
        {
            if (WithDefinition(modsFolder, name) is { } folder)
                providers.Add(Read(folder, name, false, false, unreadable));
        }

        return new ContentProviders(providers, unreadable);
    }

    private static async Task<List<ModManifestEntryDto>> ReadManifestAsync(string path, List<string> unreadable, CancellationToken cancellationToken)
    {
        try
        {
            return (await TomlFileStore.ReadAsync<ManifestDto>(path, cancellationToken).ConfigureAwait(false))?.Mods ?? [];
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            unreadable.Add(path);
            return [];
        }
    }

    private static string? WithDefinition(string? root, string id) =>
        root is not null && ModFolders.Find(root, id) is { } folder && File.Exists(Path.Combine(folder, ModFolders.DefinitionFileName)) ? folder : null;

    private static ContentProvider Read(string folder, string id, bool enabled, bool isGameContent, List<string> unreadable)
    {
        var defines = new ContentIds();
        var definition = Path.Combine(folder, ModFolders.DefinitionFileName);
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(definition)) ?? new TomlTable();
        }
        catch (Exception exception) when (exception is TomlException or IOException or UnauthorizedAccessException)
        {
            unreadable.Add(definition);
            return new ContentProvider(id, enabled, isGameContent, defines);
        }

        foreach (var asset in Strings(table, "assets"))
            ReadFile(Path.Combine(folder, asset), defines, system: false, unreadable);
        foreach (var system in Strings(table, "systems"))
            ReadFile(Path.Combine(folder, system), defines, system: true, unreadable);

        return new ContentProvider(id, enabled, isGameContent, defines);
    }

    private static IEnumerable<string> Strings(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is TomlArray array ? array.OfType<string>() : [];

    /// <summary>
    /// Reads the ids that the direct children of the root define, and the id of the root of a system file.
    /// LoadFromLibrary in a system file is a reference to a body of an asset file, not a definition.
    /// </summary>
    private static void ReadFile(string path, ContentIds defines, bool system, List<string> unreadable)
    {
        var found = new ContentIds();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = XmlReader.Create(stream, Settings);
            string? substance = null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (reader.Depth == 0 && system)
                {
                    found.Add(ContentIdKind.System, reader.GetAttribute("Id"));
                }
                else if (reader.Depth == 1)
                {
                    var element = reader.LocalName;
                    var id = reader.GetAttribute("Id");
                    substance = element == "Substance" ? id : null;
                    if (!system && element is "Part" or "SubPart")
                        found.Add(ContentIdKind.Part, id);
                    else if (!system && element == "Character")
                        found.Add(ContentIdKind.Character, id);
                    else if (BodyElements.Contains(element))
                        found.Add(ContentIdKind.Body, id);
                }
                else if (reader.Depth == 2 && !string.IsNullOrWhiteSpace(substance) && Phases.TryGetValue(reader.LocalName, out var phase))
                {
                    found.Add(ContentIdKind.SubstancePhase, substance + phase);
                }
            }
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            unreadable.Add(path);
            return;
        }

        foreach (var id in found)
            defines.Add(id.Kind, id.Id);
    }
}
