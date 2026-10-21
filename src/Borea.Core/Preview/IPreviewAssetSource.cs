namespace Borea.Core.Preview;

/// <summary>
/// Read access to the files of the game and of the mods that a preview uses. It only reads, and it never writes or
/// extracts a file. Disposing it releases the open archives and the cached entries.
/// </summary>
public interface IPreviewAssetSource : IDisposable
{
    /// <summary>
    /// Opens the file for random access. The stream can always seek, and the caller disposes it. Returns null when the
    /// source or the file is not there, or when the file is larger than the cap of the source.
    /// </summary>
    Stream? TryOpenRead(PreviewAssetRef asset);

    /// <summary>
    /// Opens the file for one forward pass over its start, such as the header and JSON chunk of a GLB. The stream may
    /// not seek, and it stops at a byte cap, so a caller reads only what it needs and then disposes it. Returns null
    /// when the source or the file is not there.
    /// </summary>
    Stream? TryOpenHead(PreviewAssetRef asset);

    /// <summary>
    /// A key that is equal for two references only when they open the same bytes, so a cache of decoded meshes and
    /// textures can share entries between scenes.
    /// </summary>
    string GetCacheKey(PreviewAssetRef asset);
}
