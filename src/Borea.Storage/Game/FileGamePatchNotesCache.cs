using Borea.Core.Game;
using Borea.Core.Paths;
using Borea.Storage.Files;

namespace Borea.Storage.Game;

/// <summary>Keeps downloaded patch notes files in the GamePatchNotes folder of the Borea data folder.</summary>
public sealed class FileGamePatchNotesCache : IGamePatchNotesCache
{
    private readonly IGamePathProvider _paths;

    public FileGamePatchNotesCache(IGamePathProvider paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = PathOf(fileName);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > GamePatchNotesFile.MaxDownloadBytes)
                return null;

            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Task WriteAsync(string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        => AtomicFile.WriteAllBytesAsync(PathOf(fileName), bytes.ToArray(), cancellationToken);

    private string PathOf(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.StartsWith('.') || !fileName.EndsWith(".json", StringComparison.Ordinal)
            || !fileName.All(character => char.IsAsciiLetterOrDigit(character) || character == '.'))
        {
            throw new ArgumentException("The file name must be a published patch notes file name.", nameof(fileName));
        }

        return Path.Combine(_paths.GetGamePatchNotesFolder(), fileName);
    }
}
