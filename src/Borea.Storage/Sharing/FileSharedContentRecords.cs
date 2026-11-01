using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Sharing;
using Borea.Storage.Mods;
using Borea.Storage.Toml;

namespace Borea.Storage.Sharing;

public sealed class FileSharedContentRecords : ISharedContentRecords
{
    private readonly IGamePathProvider _paths;
    private readonly IBoreaLog? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileSharedContentRecords(IGamePathProvider paths, IBoreaLog? log = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _log = log;
    }

    public async Task<IReadOnlyList<SharedContentEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var dto = await ReadLockedAsync(cancellationToken).ConfigureAwait(false);
        return dto is null ? [] : dto.Items.Select(FromDto).ToList();
    }

    public async Task<SharedContentAuthor?> GetAuthorAsync(CancellationToken cancellationToken = default)
    {
        var dto = await ReadLockedAsync(cancellationToken).ConfigureAwait(false);
        return dto?.Author is { } author ? new SharedContentAuthor(author.Authors, author.License) : null;
    }

    public Task RecordAsync(SharedContentEntry entry, SharedContentAuthor? author, CancellationToken cancellationToken = default) =>
        ChangeAsync(dto =>
        {
            dto.Items.RemoveAll(item => item.HostKey == entry.HostKey && Matches(item, entry.Type, entry.FolderName));
            dto.Items.Add(ToDto(entry));
            if (author is not null)
                dto.Author = new SharedContentAuthorDto { Authors = author.Authors.ToList(), License = author.License };

            return true;
        }, cancellationToken);

    public Task RenameFolderAsync(ContentType type, string oldFolderName, string newFolderName, CancellationToken cancellationToken = default) =>
        ChangeAsync(dto =>
        {
            var moved = dto.Items.Where(item => Matches(item, type, oldFolderName)).ToList();
            // An older entry of the new name is about a folder that is gone or in another instance, so it must not name a listing for the renamed item.
            var dropped = dto.Items.RemoveAll(item => Matches(item, type, newFolderName) && !moved.Contains(item));
            foreach (var item in moved)
                item.Folder = newFolderName;

            return moved.Count > 0 || dropped > 0;
        }, cancellationToken);

    private async Task<SharedContentDto?> ReadLockedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <param name="change">Returns whether there is something to write.</param>
    private async Task ChangeAsync(Func<SharedContentDto, bool> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A file that cannot be read may still hold what the player needs, so nothing writes over it.
            if (await ReadAsync(cancellationToken).ConfigureAwait(false) is not { } dto || !change(dto))
                return;

            var path = _paths.GetSharedContentPath();
            try
            {
                await TomlFileStore.WriteAsync(path, dto, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log?.Write($"Borea could not write {path}.", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Null when the file cannot be read, and an empty record when it does not exist.</summary>
    private async Task<SharedContentDto?> ReadAsync(CancellationToken cancellationToken)
    {
        var path = _paths.GetSharedContentPath();
        try
        {
            return await TomlFileStore.ReadAsync<SharedContentDto>(path, cancellationToken).ConfigureAwait(false) ?? new SharedContentDto();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log?.Write($"Borea could not read {path}.", exception);
            return null;
        }
    }

    private static bool Matches(SharedContentItemDto item, ContentType type, string folderName) =>
        MetadataEnumMapper.ParseContentType(item.Type) == type && string.Equals(item.Folder, folderName, StringComparison.OrdinalIgnoreCase);

    private static SharedContentItemDto ToDto(SharedContentEntry entry) => new()
    {
        Type = MetadataEnumMapper.ToDto(entry.Type),
        HostKey = entry.HostKey,
        HostName = entry.HostName,
        ListingId = entry.ListingId,
        Folder = entry.FolderName,
        Version = entry.Version,
        Page = entry.Page?.AbsoluteUri,
    };

    private static SharedContentEntry FromDto(SharedContentItemDto item) => new(
        MetadataEnumMapper.ParseContentType(item.Type),
        item.HostKey,
        item.HostName,
        item.ListingId,
        item.Folder,
        item.Version,
        Uri.TryCreate(item.Page, UriKind.Absolute, out var page) ? page : null);
}
