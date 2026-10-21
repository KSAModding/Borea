namespace Borea.Core.Preview;

/// <summary>
/// Read access to the files of the game and of the mods that a preview uses. It only reads, and it never writes or
/// extracts a file. Disposing it releases the open archives and the cached entries.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TryOpenRead"/>, <see cref="TryOpenHead"/> and <see cref="GetCacheKey"/> are safe to call from several
/// threads at the same time, because a scene build loads assets on parallel jobs. An implementation that reads a zip
/// archive serializes its access to the archive internally.
/// </para>
/// <para>
/// A call to a member after <see cref="IDisposable.Dispose"/> throws <see cref="ObjectDisposedException"/>. A stream
/// that <see cref="TryOpenRead"/> returned stays valid after the dispose, because it is a memory stream over bytes that
/// the dispose does not release. A stream that <see cref="TryOpenHead"/> returned can read from the open archive, so it can throw
/// <see cref="ObjectDisposedException"/> after the dispose.
/// </para>
/// <para>
/// An <see cref="IOException"/> from a member or from a returned stream means that the source is gone. A caller treats
/// <see cref="ObjectDisposedException"/> and <see cref="OperationCanceledException"/> as a cancellation of its work,
/// not as a failure of the asset.
/// </para>
/// </remarks>
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
