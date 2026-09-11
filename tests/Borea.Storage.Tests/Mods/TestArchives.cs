using System.IO.Compression;

namespace Borea.Storage.Tests.Mods;

/// <summary>
/// Builds the zip archives the install tests use.
/// </summary>
internal static class TestArchives
{
    public static byte[] Build(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
