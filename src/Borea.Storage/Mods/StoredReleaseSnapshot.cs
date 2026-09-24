using Borea.Storage.Toml;

namespace Borea.Storage.Mods;

/// <summary>
/// The file count, total size and newest write time of a stored release. A mod
/// that writes into its own folder changes at least one of them.
/// </summary>
internal sealed record StoredReleaseSnapshot(long Files, long Bytes, DateTimeOffset NewestWrite)
{
    /// <summary>FAT keeps write times to two seconds, so a library moved to such a drive still matches.</summary>
    private static readonly TimeSpan WriteTimeTolerance = TimeSpan.FromSeconds(2);

    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static StoredReleaseSnapshot Measure(string folder)
    {
        long files = 0;
        long bytes = 0;
        var newestWrite = DateTime.MinValue;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", AllFiles))
        {
            files++;
            bytes += file.Length;
            if (file.LastWriteTimeUtc > newestWrite)
                newestWrite = file.LastWriteTimeUtc;
        }

        return new StoredReleaseSnapshot(files, bytes, new DateTimeOffset(newestWrite, TimeSpan.Zero));
    }

    public bool Matches(StoredReleaseSnapshot other)
        => Files == other.Files && Bytes == other.Bytes && (NewestWrite - other.NewestWrite).Duration() <= WriteTimeTolerance;

    /// <summary>Null when the file is missing.</summary>
    public static async Task<StoredReleaseSnapshot?> ReadAsync(string path, CancellationToken cancellationToken = default)
        => await TomlFileStore.ReadAsync<StoredReleaseSnapshotDto>(path, cancellationToken).ConfigureAwait(false) is { } dto
            ? new StoredReleaseSnapshot(dto.Files, dto.Bytes, dto.NewestWrite)
            : null;

    public Task WriteAsync(string path, CancellationToken cancellationToken = default)
        => TomlFileStore.WriteAsync(path, new StoredReleaseSnapshotDto { Files = Files, Bytes = Bytes, NewestWrite = NewestWrite }, cancellationToken);
}
