using System.IO.Compression;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Listings;

/// <summary>Reads a release archive with the rules of derive_root, read_mod_toml and derived_dependencies of the stamper in content-index-releases.</summary>
public static class ListingArchive
{
    public const string ModTomlName = "mod.toml";

    /// <summary>The stamper reads a mod.toml of at most this many bytes.</summary>
    public const long ModTomlLimit = 1024 * 1024;

    /// <summary>Reads the facts of a release archive.</summary>
    /// <param name="installRoot">
    /// The install root that the listing authors, or null when the stamper derives it. The mod.toml of that root gives the
    /// dependencies, as the stamper reads it.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The archive is not a readable zip, the authored install root is not in it, or the mod.toml is not valid TOML or has
    /// dependencies the stamper cannot read.
    /// </exception>
    public static ListingArchiveFacts Read(string archivePath, string? installRoot = null)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(archivePath);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"the archive is not a readable zip, {exception.Message}", exception);
        }

        using (archive)
        {
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            var folders = TopLevelFolders(names);
            var withManifest = folders.Where(folder => names.Contains($"{folder}/{ModTomlName}", StringComparer.Ordinal)).ToList();
            var candidates = withManifest.Count > 0 ? withManifest : folders;
            var root = candidates.Count == 1 ? candidates[0] : null;
            var dependencyRoot = installRoot is null ? root : AuthoredRoot(installRoot, archive);
            var dependencyToml = dependencyRoot is null ? null : archive.GetEntry(dependencyRoot.Length == 0 ? ModTomlName : $"{dependencyRoot}/{ModTomlName}");
            var dependencies = dependencyToml is null ? null : ModDependencies(ReadModToml(dependencyToml));
            if (root is null)
                return new ListingArchiveFacts(null, folders, ModToml: false, EntryAssembly: null, IsCodeMod: false) { ModDependencies = dependencies };

            var modToml = archive.GetEntry($"{root}/{ModTomlName}");
            var manifest = modToml is null ? null : ReadModToml(modToml);
            var entryAssembly = manifest is null ? root : EntryAssembly(manifest) ?? root;
            var assemblyPath = $"{root}/{entryAssembly}.dll";
            var isCodeMod = names.Any(name => string.Equals(name.Replace('\\', '/'), assemblyPath, StringComparison.OrdinalIgnoreCase));
            return new ListingArchiveFacts(root, folders, modToml is not null, entryAssembly, isCodeMod) { ModDependencies = dependencies };
        }
    }

    /// <summary>The folders at the root of the archive in archive order. A file at the root names none.</summary>
    public static IReadOnlyList<string> TopLevelFolders(IEnumerable<string> entryNames)
    {
        var seen = new List<string>();
        foreach (var entry in entryNames)
        {
            var name = entry.Replace('\\', '/');
            var slash = name.IndexOf('/');
            var head = slash < 0 ? name : name[..slash];
            var rest = slash < 0 ? string.Empty : name[(slash + 1)..];
            if (head.Length == 0 || (rest.Length == 0 && !name.EndsWith('/')))
                continue;

            if (!seen.Contains(head, StringComparer.Ordinal))
                seen.Add(head);
        }

        return seen;
    }

    private static TomlTable ReadModToml(ZipArchiveEntry modToml)
    {
        if (modToml.Length > ModTomlLimit)
            throw new InvalidDataException($"the archive's {modToml.FullName} is {modToml.Length} bytes, above the {ModTomlLimit} byte limit");

        string text;
        try
        {
            using var reader = new StreamReader(modToml.Open(), new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true));
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.DecoderFallbackException)
        {
            throw new InvalidDataException($"the archive's {modToml.FullName} cannot be read, {exception.Message}", exception);
        }

        try
        {
            return TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException exception)
        {
            throw new InvalidDataException($"the archive's {modToml.FullName} is not valid TOML, {exception.Message}", exception);
        }
    }

    private static string? EntryAssembly(TomlTable manifest) =>
        manifest.TryGetValue("StarMap", out var starMap) && starMap is TomlTable section
            && section.TryGetValue("EntryAssembly", out var value) && value is string name && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : null;

    /// <summary>
    /// The blocks of [[StarMap.ModDependencies]] as derived_dependencies of the stamper reads them: the ModId without the
    /// spaces around it, and Optional with the truth of a Python value, so a missing one is false. An empty value or a
    /// missing one declares no dependency.
    /// </summary>
    /// <exception cref="InvalidDataException">The stamper cannot read the dependencies, so it stamps no release.</exception>
    private static IReadOnlyList<LocalModDependency> ModDependencies(TomlTable manifest)
    {
        if (!manifest.TryGetValue("StarMap", out var starMap) || !IsTrue(starMap))
            return [];
        if (starMap is not TomlTable section)
            throw new InvalidDataException("[StarMap] of the mod.toml is not a table");
        if (!section.TryGetValue("ModDependencies", out var value) || !IsTrue(value))
            return [];

        IEnumerable<object?> blocks = value switch
        {
            TomlTableArray tables => tables,
            TomlArray array => array,
            _ => throw new InvalidDataException("StarMap.ModDependencies of the mod.toml is not a list of [[StarMap.ModDependencies]] blocks"),
        };

        var dependencies = new List<LocalModDependency>();
        foreach (var item in blocks)
        {
            if (item is not TomlTable block)
                throw new InvalidDataException("a [[StarMap.ModDependencies]] block is not a table");

            block.TryGetValue("ModId", out var modId);
            if (!IsTrue(modId))
                throw new InvalidDataException("a [[StarMap.ModDependencies]] block carries no ModId");
            if (modId is not string text)
                throw new InvalidDataException("a [[StarMap.ModDependencies]] block has a ModId that is not text");
            if (text.Trim().Length == 0)
                throw new InvalidDataException("a [[StarMap.ModDependencies]] block carries no ModId");

            dependencies.Add(new LocalModDependency(text.Trim(), block.TryGetValue("Optional", out var optional) && IsTrue(optional)));
        }

        return dependencies;
    }

    /// <summary>The authored install root as relative_path of the stamper makes it, which is empty for the archive root.</summary>
    /// <exception cref="InvalidDataException">The root is no relative path, or the archive has no file under it.</exception>
    private static string AuthoredRoot(string installRoot, ZipArchive archive)
    {
        if (installRoot.Length == 0 || installRoot[0] is '/' or '~' || installRoot.Contains('\\', StringComparison.Ordinal)
            || (installRoot.Length > 1 && char.IsAsciiLetter(installRoot[0]) && installRoot[1] == ':'))
            throw new InvalidDataException($"the authored install root '{installRoot}' is not a relative path with '/' separators");

        var parts = new List<string>();
        foreach (var part in installRoot.Split('/'))
        {
            if (part is "" or ".")
                continue;
            if (part != "..")
                parts.Add(part);
            else if (parts.Count > 0)
                parts.RemoveAt(parts.Count - 1);
            else
                throw new InvalidDataException($"the authored install root '{installRoot}' escapes its anchor");
        }

        var root = string.Join('/', parts);
        if (root.Length > 0 && !archive.Entries.Any(entry => !entry.FullName.EndsWith('/') && entry.FullName.Replace('\\', '/').StartsWith(root + "/", StringComparison.Ordinal)))
            throw new InvalidDataException($"the authored install root '{root}' is not in the archive");

        return root;
    }

    private static bool IsTrue(object? value) => value switch
    {
        bool flag => flag,
        string text => text.Length > 0,
        long number => number != 0,
        double number => number != 0,
        TomlArray array => array.Count > 0,
        TomlTable table => table.Count > 0,
        null => false,
        _ => true,
    };
}
