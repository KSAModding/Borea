using System.IO.Compression;
using Borea.Core.Listings;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Listings;

/// <summary>Reads a release archive with the rules of derive_root and read_mod_toml of the stamper in content-index-releases.</summary>
public static class ListingArchive
{
    public const string ModTomlName = "mod.toml";

    /// <summary>The stamper reads a mod.toml of at most this many bytes.</summary>
    public const long ModTomlLimit = 1024 * 1024;

    /// <exception cref="InvalidDataException">The archive is not a readable zip, or its mod.toml is not valid TOML.</exception>
    public static ListingArchiveFacts Read(string archivePath)
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
            if (candidates.Count != 1)
                return new ListingArchiveFacts(null, folders, ModToml: false, EntryAssembly: null, IsCodeMod: false);

            var root = candidates[0];
            var modToml = archive.GetEntry($"{root}/{ModTomlName}");
            var entryAssembly = modToml is null ? root : EntryAssembly(modToml) ?? root;
            var assemblyPath = $"{root}/{entryAssembly}.dll";
            var isCodeMod = names.Any(name => string.Equals(name.Replace('\\', '/'), assemblyPath, StringComparison.OrdinalIgnoreCase));
            return new ListingArchiveFacts(root, folders, modToml is not null, entryAssembly, isCodeMod);
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

    private static string? EntryAssembly(ZipArchiveEntry modToml)
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

        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException exception)
        {
            throw new InvalidDataException($"the archive's {modToml.FullName} is not valid TOML, {exception.Message}", exception);
        }

        return table.TryGetValue("StarMap", out var starMap) && starMap is TomlTable section
            && section.TryGetValue("EntryAssembly", out var value) && value is string name && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : null;
    }
}
