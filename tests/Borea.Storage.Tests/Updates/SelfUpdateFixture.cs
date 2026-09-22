using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Storage.Tests.Updates;

/// <summary>Serves the files of one made-up release out of a folder the test owns.</summary>
internal sealed class FakeReleaseFiles : IBoreaReleaseFiles
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>The URLs a caller asked for, in order.</summary>
    public List<string> Requested { get; } = [];

    /// <summary>When set, every download fails with it.</summary>
    public Exception? DownloadFailure { get; set; }

    public void Add(string name, byte[] content) => _files[Url(name)] = content;

    public void Add(string name, string content) => Add(name, Encoding.UTF8.GetBytes(content));

    public static string Url(string name) => "https://github.com/KSAModding/Borea/releases/download/v0.2.0/" + name;

    public Task<string?> ReadTextAsync(string url, CancellationToken cancellationToken = default)
    {
        Requested.Add(url);
        return Task.FromResult(_files.TryGetValue(url, out var content) ? Encoding.UTF8.GetString(content) : null);
    }

    public async Task<string> DownloadAsync(string url, string path, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Requested.Add(url);
        if (DownloadFailure is not null)
            throw DownloadFailure;

        if (!_files.TryGetValue(url, out var content))
            throw new HttpRequestException($"No release file is served at '{url}'.");

        await File.WriteAllBytesAsync(path, content, cancellationToken);
        progress?.Report(new DownloadProgress(content.Length, content.Length));
        return Convert.ToHexString(SHA256.HashData(content));
    }
}

/// <summary>Records the handover instead of starting a program.</summary>
internal sealed class RecordingHandoverStarter : Borea.Storage.Updates.IHandoverStarter
{
    public string? ProgramPath { get; private set; }

    public IReadOnlyList<string> Arguments { get; private set; } = [];

    public Exception? Failure { get; set; }

    public void Start(string programPath, IReadOnlyList<string> arguments)
    {
        if (Failure is not null)
            throw Failure;

        ProgramPath = programPath;
        Arguments = arguments;
    }
}

/// <summary>Builds the release archives the workflow publishes, with the same layout.</summary>
internal static class ReleaseArchiveBuilder
{
    /// <summary>A zip that holds one folder with the program, the license and the notices in it.</summary>
    /// <param name="extraPath">One more entry below the folder, which a Borea release never holds.</param>
    public static byte[] Zip(string folderName, string programName, string programContent, string? extraPath = null)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in Files(programName, programContent))
                Write(archive, $"{folderName}/{name}", content);

            if (extraPath is not null)
                Write(archive, $"{folderName}/{extraPath}", "more than a build");
        }

        return bytes.ToArray();
    }

    /// <summary>The same layout as a gzip-compressed tar, which is what the other platforms get.</summary>
    public static byte[] TarGz(string folderName, string programName, string programContent)
    {
        using var bytes = new MemoryStream();
        using (var compressed = new GZipStream(bytes, CompressionMode.Compress, leaveOpen: true))
        using (var archive = new TarWriter(compressed, TarEntryFormat.Pax, leaveOpen: true))
        {
            archive.WriteEntry(new PaxTarEntry(TarEntryType.Directory, folderName + "/"));
            foreach (var (name, content) in Files(programName, programContent))
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, $"{folderName}/{name}")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                };
                if (name == programName)
                    entry.Mode |= UnixFileMode.UserExecute;

                archive.WriteEntry(entry);
            }
        }

        return bytes.ToArray();
    }

    /// <summary>A checksums file in the format of the sha256sum program.</summary>
    public static string Checksums(params (string Name, byte[] Content)[] files)
        => string.Concat(files.Select(file => $"{Convert.ToHexStringLower(SHA256.HashData(file.Content))}  {file.Name}\n"));

    private static IEnumerable<(string Name, string Content)> Files(string programName, string programContent)
    {
        yield return (programName, programContent);
        yield return ("LICENSE", "MIT");
        yield return ("THIRD-PARTY-NOTICES.txt", "Avalonia");
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var entry = archive.CreateEntry(name).Open();
        using var writer = new StreamWriter(entry);
        writer.Write(content);
    }
}
