using System.Globalization;

namespace Borea.Storage.Preview.Sources;

/// <summary>
/// The files of a mod folder on disk, such as Content/Core of the game or a mod in an instance. It opens files for
/// reading only and holds no handle between calls.
/// </summary>
/// <remarks>
/// The game opens an asset through Path.Combine and FileInfo (FileReference.ModPath), which on Windows finds a file
/// whatever the case of the name. A file system that compares names exactly, as on Linux, would not find a mesh the
/// author named in another case, so after the exact lookup each segment is matched with
/// <see cref="StringComparison.OrdinalIgnoreCase"/>.
/// </remarks>
internal sealed class FolderPreviewFiles : IPreviewFiles
{
    private readonly string _root;

    public FolderPreviewFiles(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string Root => _root;

    public bool Exists(string path) => Resolve(path) is not null;

    public Stream? TryOpenHead(string path)
    {
        if (PreviewPath.Normalize(path) is not { } normalized || Open(normalized) is not { } stream)
            return null;

        return new BoundedCountingStream(stream, PreviewFileCaps.Default.For(normalized));
    }

    public Stream? TryOpen(string path)
    {
        if (PreviewPath.Normalize(path) is not { } normalized || Open(normalized) is not { } stream)
            return null;

        try
        {
            if (stream.Length <= PreviewFileCaps.Default.For(normalized))
                return stream;
        }
        catch (IOException)
        {
        }

        stream.Dispose();
        return null;
    }

    public string? Describe(string path)
    {
        if (Resolve(path) is not { } file || Info(file) is not { } info)
            return null;

        return string.Create(CultureInfo.InvariantCulture, $"file:{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
    }

    public void Dispose()
    {
    }

    private static FileInfo? Info(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists ? info : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private FileStream? Open(string normalized)
    {
        if (ResolveNormalized(normalized) is not { } file)
            return null;

        try
        {
            return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? Resolve(string path) => PreviewPath.Normalize(path) is { } normalized ? ResolveNormalized(normalized) : null;

    private string? ResolveNormalized(string normalized)
    {
        var exact = Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(exact))
            return exact;

        return OperatingSystem.IsWindows() ? null : ResolveIgnoringCase(normalized);
    }

    private string? ResolveIgnoringCase(string normalized)
    {
        var current = _root;
        var segments = normalized.Split('/');
        try
        {
            for (var index = 0; index < segments.Length; index++)
            {
                var last = index == segments.Length - 1;
                var candidates = last ? Directory.EnumerateFiles(current) : Directory.EnumerateDirectories(current);
                var match = candidates.FirstOrDefault(candidate =>
                    string.Equals(Path.GetFileName(candidate), segments[index], StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return null;

                current = match;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return current;
    }
}
