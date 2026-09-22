using System.Formats.Tar;
using System.IO.Compression;

namespace Borea.Storage.Updates;

/// <summary>Unpacks a Borea release archive. Each one holds exactly one folder with the build in it.</summary>
internal static class ReleaseArchive
{
    /// <summary>
    /// Unpacks <paramref name="archive"/> into <paramref name="destination"/> and returns the one
    /// folder the archive holds. The caller keeps the archive open, so that the bytes which were
    /// checked are the bytes that are unpacked. Both unpackers refuse an entry that would land
    /// outside the destination.
    /// </summary>
    /// <param name="archiveName">The published name of the archive, which says how it is packed.</param>
    /// <exception cref="InvalidOperationException">The archive does not hold exactly one folder and nothing else.</exception>
    public static string Unpack(Stream archive, string archiveName, string destination)
    {
        ArgumentNullException.ThrowIfNull(archive);
        Directory.CreateDirectory(destination);

        if (archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
        }
        else
        {
            using var unpacked = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
            TarFile.ExtractToDirectory(unpacked, destination, overwriteFiles: true);
        }

        var folders = Directory.GetDirectories(destination);
        if (folders.Length != 1 || Directory.GetFiles(destination).Length != 0)
            throw new InvalidOperationException($"The archive '{archiveName}' does not hold one folder with the build in it.");

        return folders[0];
    }
}
