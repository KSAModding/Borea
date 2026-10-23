namespace Borea.Storage.Preview.Sources;

/// <summary>
/// Read-only access to the files of one mod, from its folder or from its archive. Every path is relative to the mod
/// folder and goes through <see cref="PreviewPath.Normalize"/>, so a rooted path or a ".." segment finds nothing.
/// Nothing is extracted or written. A file that is not there, that cannot be read, or that is larger than the cap of
/// the source gives null.
/// </summary>
internal interface IPreviewFiles : IDisposable
{
    bool Exists(string path);

    /// <summary>
    /// A forward-only stream over the start of the file that ends at the cap. The caller disposes it after one pass.
    /// </summary>
    Stream? TryOpenHead(string path);

    /// <summary>A seekable stream over the whole file. The caller disposes it.</summary>
    Stream? TryOpen(string path);

    /// <summary>
    /// A key that changes when the bytes of the file can have changed: the absolute path with the length and the
    /// last write time for a folder, the archive SHA-256 with the entry name for a zip. Null when the file is not there.
    /// </summary>
    string? Describe(string path);
}

/// <summary>The largest file a source reads, counted in real bytes.</summary>
/// <param name="TextBytes">For .xml and .toml files, which go whole into a parser.</param>
/// <param name="OtherBytes">For every other file, such as a GLB mesh or a KTX2 texture.</param>
internal sealed record PreviewFileCaps(long TextBytes, long OtherBytes)
{
    public const long DefaultArchiveTotalBytes = 512L * 1024 * 1024;

    public static PreviewFileCaps Default { get; } = new(32L * 1024 * 1024, 256L * 1024 * 1024);

    public long For(string normalizedPath) => PreviewPath.IsText(normalizedPath) ? TextBytes : OtherBytes;

    /// <summary>
    /// The decompressed bytes one archive may give in total: a quarter more than the unpacked size the release states,
    /// or <see cref="DefaultArchiveTotalBytes"/> when it states none.
    /// </summary>
    public static long ArchiveTotal(long? installSizeBytes) =>
        installSizeBytes is long size && size > 0 && size <= long.MaxValue / 5 * 4
            ? size + size / 4
            : DefaultArchiveTotalBytes;
}
