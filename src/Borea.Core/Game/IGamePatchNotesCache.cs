namespace Borea.Core.Game;

/// <summary>Published patch notes files kept by their file name, because a published file never changes.</summary>
public interface IGamePatchNotesCache
{
    /// <summary>The bytes stored under <paramref name="fileName"/>, or null when none are stored.</summary>
    Task<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken = default);

    Task WriteAsync(string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
}
