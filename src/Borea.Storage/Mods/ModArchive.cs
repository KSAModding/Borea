using System.IO.Compression;

namespace Borea.Storage.Mods;

/// <summary>
/// Reads the part of a release archive that becomes the installed folder.
/// </summary>
internal static class ModArchive
{
    private static readonly string[] ZipContentTypes = { "application/zip", "application/x-zip-compressed" };

    /// <summary>
    /// The second media type is what some hosts send for the same bytes.
    /// </summary>
    public static bool IsZip(string contentType) => ZipContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The root RFC 0035 rule 9 derives for a mod whose release states none:
    /// the one top-level directory that holds a mod.toml. Any other layout,
    /// a mod.toml at the top level included, derives to the archive root, which
    /// is null.
    /// </summary>
    public static string? DeriveRoot(string archivePath) => DeriveRoot(archivePath, [ModFolders.DefinitionFileName]);

    /// <summary>
    /// The same rule for content whose root holds several files: the archive root when every marker is
    /// at the top level, else the one top-level directory that holds every marker, else the archive root.
    /// </summary>
    public static string? DeriveRoot(string archivePath, IReadOnlyList<string> markers)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var names = archive.Entries.Select(entry => Normalize(entry.FullName)).ToList();

        if (markers.All(marker => names.Contains(marker, StringComparer.Ordinal)))
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

        return markers.All(marker => names.Contains($"{single}/{marker}", StringComparer.Ordinal)) ? single : null;
    }

    /// <summary>
    /// Writes every entry below <paramref name="root"/> into
    /// <paramref name="destination"/> with the root stripped, and returns how
    /// many files that was. A null root is the archive root. An entry whose
    /// path would land outside the destination fails the whole extraction,
    /// because the archive is the author's bytes and not ours. So does an
    /// entry with a ':' below the root, because Windows writes notes.txt:x as
    /// a hidden stream of notes.txt and Setup.exe::$DATA as Setup.exe.
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

            if (relative.Contains(':'))
                throw new InvalidOperationException($"The archive entry '{entry.FullName}' has a ':' in its name, which Windows reads as a stream of another file.");

            var target = Path.GetFullPath(Path.Combine(destinationRoot, relative));
            if (!target.StartsWith(inside, StringComparison.Ordinal))
                throw new InvalidOperationException($"The archive entry '{entry.FullName}' would land outside the install folder.");

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
