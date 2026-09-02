using System.IO.Compression;

namespace Borea.Storage.Mods;

/// <summary>
/// Reads the part of a release archive that becomes the installed folder.
/// </summary>
internal static class ModArchive
{
    /// <summary>
    /// The root RFC 0035 rule 9 derives for a mod whose release states none:
    /// the one top-level directory that holds a mod.toml. Any other layout,
    /// a mod.toml at the top level included, derives to the archive root, which
    /// is null.
    /// </summary>
    public static string? DeriveRoot(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var names = archive.Entries.Select(entry => Normalize(entry.FullName)).ToList();

        if (names.Contains(ModFolders.DefinitionFileName, StringComparer.Ordinal))
            return null;

        string? single = null;
        foreach (var name in names)
        {
            var slash = name.IndexOf('/');
            if (slash < 0)
                continue;

            var directory = name[..slash];
            if (single is null)
                single = directory;
            else if (!string.Equals(single, directory, StringComparison.Ordinal))
                return null;
        }

        if (single is null)
            return null;

        return names.Contains($"{single}/{ModFolders.DefinitionFileName}", StringComparer.Ordinal) ? single : null;
    }

    /// <summary>
    /// Writes every entry below <paramref name="root"/> into
    /// <paramref name="destination"/> with the root stripped, and returns how
    /// many files that was. A null root is the archive root. An entry whose
    /// path would land outside the destination fails the whole extraction,
    /// because the archive is the author's bytes and not ours.
    /// </summary>
    public static int Extract(string archivePath, string? root, string destination)
    {
        var prefix = root is null ? string.Empty : root.TrimEnd('/') + "/";
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var inside = destinationRoot + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(destinationRoot);

        var files = 0;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var name = Normalize(entry.FullName);
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var relative = name[prefix.Length..];
            if (relative.Length == 0)
                continue;

            var target = Path.GetFullPath(Path.Combine(destinationRoot, relative));
            if (!target.StartsWith(inside, StringComparison.Ordinal))
                throw new InvalidOperationException($"The archive entry '{entry.FullName}' would land outside the mod folder.");

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            files++;
        }

        return files;
    }

    /// <summary>
    /// The entry name with '/' as the separator and without the "./" some
    /// archivers put in front of every entry.
    /// </summary>
    private static string Normalize(string entryName)
    {
        var name = entryName.Replace('\\', '/');
        while (name.StartsWith("./", StringComparison.Ordinal))
            name = name[2..];

        return name;
    }
}
