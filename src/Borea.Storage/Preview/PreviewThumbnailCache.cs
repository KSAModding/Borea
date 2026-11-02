using System.Text;
using Borea.Core.Logging;
using Borea.Core.Paths;
using Borea.Core.Preview;

namespace Borea.Storage.Preview;

/// <summary>
/// The still images of vehicle previews, one file thumbs/&lt;sha256&gt;-&lt;build&gt;.png below
/// <see cref="IGamePathProvider.GetPreviewCacheFolder"/>, with the vehicle hash in lowercase hex, and next to it a
/// file of the same name with the extension .key that holds the scene key the still was made from.
/// </summary>
/// <remarks>
/// The cache stores bytes only and decodes nothing. A store writes each file to a .part file with a unique name and
/// then moves it over the final name, the PNG first, so a reader sees a whole old still or a whole new one, and a key
/// file never claims a still that is not there yet. The last write time of a still is the time it was stored, and
/// the eviction removes the oldest stills first. Nothing in the constructor reads or writes the disk.
/// </remarks>
public sealed class PreviewThumbnailCache : IPreviewThumbnails
{
    /// <summary>The total size of the stills that the eviction keeps.</summary>
    public const long DefaultCapacityBytes = 64L * 1024 * 1024;

    /// <summary>The largest PNG that a store accepts, far above a still of 384 by 216 pixels.</summary>
    public const int MaxStillBytes = 4 * 1024 * 1024;

    private const string FolderName = "thumbs";
    private const string StillExtension = ".png";
    private const string KeyExtension = ".key";
    private const string PartExtension = ".part";
    private const int MaxBuildLength = 64;
    private const int MaxSceneKeyLength = 128;

    private readonly string _folder;
    private readonly IBoreaLog _log;
    private readonly long _capacityBytes;
    private readonly TimeProvider _time;

    public PreviewThumbnailCache(IGamePathProvider paths, IBoreaLog log, long capacityBytes = DefaultCapacityBytes, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);

        _log = log ?? throw new ArgumentNullException(nameof(log));
        _folder = Path.Combine(Path.GetFullPath(paths.GetPreviewCacheFolder()), FolderName);
        _capacityBytes = capacityBytes;
        _time = time ?? TimeProvider.System;
    }

    public string? TryGetPath(string? vehicleSha256, string? gameBuild)
    {
        if (Name(vehicleSha256, gameBuild) is not { } name)
            return null;

        var path = Path.Combine(_folder, name + StillExtension);
        return File.Exists(path) ? path : null;
    }

    public string? TryGetSceneKey(string? vehicleSha256, string? gameBuild)
    {
        if (Name(vehicleSha256, gameBuild) is not { } name)
            return null;

        try
        {
            var key = new FileInfo(Path.Combine(_folder, name + KeyExtension));
            return key.Exists && key.Length <= MaxSceneKeyLength ? File.ReadAllText(key.FullName, Encoding.ASCII) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool NeedsUpdate(string? vehicleSha256, string? gameBuild, string? sceneKey)
    {
        if (Name(vehicleSha256, gameBuild) is not { } name || !IsSceneKey(sceneKey))
            return false;

        try
        {
            var key = new FileInfo(Path.Combine(_folder, name + KeyExtension));
            if (!key.Exists || key.Length > MaxSceneKeyLength || !File.Exists(Path.Combine(_folder, name + StillExtension)))
                return true;

            return !string.Equals(File.ReadAllText(key.FullName, Encoding.ASCII), sceneKey, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A key that cannot be read is treated as missing, so the next complete preview writes it again.
            return true;
        }
    }

    public void Store(string vehicleSha256, string gameBuild, string sceneKey, byte[] pngBytes)
    {
        var name = Name(vehicleSha256, gameBuild) ?? throw new ArgumentException("The vehicle hash or the game build cannot name a still.", nameof(vehicleSha256));
        if (!IsSceneKey(sceneKey))
            throw new ArgumentException("The scene key must be ASCII letters and digits.", nameof(sceneKey));

        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length is 0 or > MaxStillBytes)
            throw new ArgumentException($"A still must have 1 to {MaxStillBytes} bytes.", nameof(pngBytes));

        var unique = Guid.NewGuid().ToString("N");
        var stillPart = Path.Combine(_folder, $"{name}{StillExtension}.{unique}{PartExtension}");
        var keyPart = Path.Combine(_folder, $"{name}{KeyExtension}.{unique}{PartExtension}");
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllBytes(stillPart, pngBytes);
            File.WriteAllText(keyPart, sceneKey, Encoding.ASCII);
            File.Move(stillPart, Path.Combine(_folder, name + StillExtension), overwrite: true);
            File.Move(keyPart, Path.Combine(_folder, name + KeyExtension), overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A Discover row that reads the old still at this moment can block the replace on Windows, and the next
            // complete preview tries again, because the key file still names the old scene.
            _log.Write($"Could not store the preview still {name}.", exception);
        }
        finally
        {
            TryDelete(stillPart);
            TryDelete(keyPart);
        }

        Evict(name);
    }

    /// <summary>
    /// Deletes the .part files older than <see cref="PreviewArchiveCache.StalePartAge"/> and the oldest stills until
    /// the rest fits in the capacity. A failure is logged and never thrown. Returns how many files it deleted.
    /// </summary>
    public int Sweep() => Evict(keptName: null);

    private int Evict(string? keptName)
    {
        var deleted = 0;
        var entries = new Dictionary<string, (long Bytes, DateTime StoredUtc, List<FileInfo> Files)>(StringComparer.Ordinal);
        try
        {
            var folder = new DirectoryInfo(_folder);
            if (!folder.Exists)
                return 0;

            var now = _time.GetUtcNow().UtcDateTime;
            foreach (var file in folder.EnumerateFiles())
            {
                if (file.Name.EndsWith(PartExtension, StringComparison.Ordinal))
                {
                    if (now - file.LastWriteTimeUtc > PreviewArchiveCache.StalePartAge && TryDelete(file.FullName))
                        deleted++;

                    continue;
                }

                var isStill = file.Extension == StillExtension;
                if (!isStill && file.Extension != KeyExtension)
                    continue;

                // A still and its key file are one entry, which is as old as its still.
                var name = Path.GetFileNameWithoutExtension(file.Name);
                var entry = entries.GetValueOrDefault(name, (0, DateTime.MinValue, []));
                entry.Files.Add(file);
                entries[name] = (entry.Bytes + file.Length, isStill ? file.LastWriteTimeUtc : entry.StoredUtc, entry.Files);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Write("Could not read the preview still folder.", exception);
            return deleted;
        }

        var total = entries.Values.Sum(entry => entry.Bytes);
        foreach (var (name, entry) in entries.OrderBy(pair => pair.Value.StoredUtc))
        {
            if (total <= _capacityBytes)
                break;

            if (string.Equals(name, keptName, StringComparison.Ordinal))
                continue;

            foreach (var file in entry.Files)
            {
                if (TryDelete(file.FullName))
                {
                    total -= file.Length;
                    deleted++;
                }
            }
        }

        return deleted;
    }

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Could not delete the preview still file {Path.GetFileName(path)}.", exception);
            return false;
        }
    }

    /// <summary>
    /// The file name without extension, or null when the hash is not 64 hex characters or the build has other
    /// characters than ASCII letters, digits, '.', '-', '+' and '_'. The build must start and end with a letter or a
    /// digit, because Windows drops a dot at the end of a file name.
    /// </summary>
    private static string? Name(string? vehicleSha256, string? gameBuild)
    {
        if (!PreviewArchiveCache.IsHash(vehicleSha256))
            return null;

        if (gameBuild is not { Length: > 0 and <= MaxBuildLength }
            || !char.IsAsciiLetterOrDigit(gameBuild[0])
            || !char.IsAsciiLetterOrDigit(gameBuild[^1])
            || !gameBuild.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' or '_'))
            return null;

        return $"{vehicleSha256!.ToLowerInvariant()}-{gameBuild}";
    }

    private static bool IsSceneKey(string? sceneKey)
        => sceneKey is { Length: > 0 and <= MaxSceneKeyLength } && sceneKey.All(char.IsAsciiLetterOrDigit);
}
