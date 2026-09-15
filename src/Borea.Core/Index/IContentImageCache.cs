namespace Borea.Core.Index;

/// <summary>Image bytes kept by their SHA-256.</summary>
public interface IContentImageCache
{
    /// <summary>The bytes stored under <paramref name="sha256"/>, or null when none are stored or they no longer have that digest.</summary>
    Task<byte[]?> ReadAsync(string sha256, CancellationToken cancellationToken = default);

    Task WriteAsync(string sha256, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
}
