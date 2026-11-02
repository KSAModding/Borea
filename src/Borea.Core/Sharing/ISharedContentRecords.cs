using Borea.Core.Mods;

namespace Borea.Core.Sharing;

/// <summary>
/// What the player published on a share host from this computer, because the index holds a new listing only hours later.
/// A file that cannot be read counts as empty and stays on the disk, and no problem with the file throws, so it never stops a share.
/// </summary>
public interface ISharedContentRecords
{
    /// <summary>The entries with the newest share last.</summary>
    Task<IReadOnlyList<SharedContentEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The authors and the license of the last new listing, or null when there was none.</summary>
    Task<SharedContentAuthor?> GetAuthorAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds the entry in place of an older one of the same type, host and folder.</summary>
    /// <param name="author">The authors and license of a new listing, or null to keep the last ones, because a new version sends none.</param>
    Task RecordAsync(SharedContentEntry entry, SharedContentAuthor? author, CancellationToken cancellationToken = default);

    /// <summary>Moves the entries of the old name to the new one, and drops every other entry of the new name, because the renamed item now holds it.</summary>
    Task RenameFolderAsync(ContentType type, string oldFolderName, string newFolderName, CancellationToken cancellationToken = default);

    /// <summary>Drops the entry of the type, host and folder, because the host says that the account does not own its listing.</summary>
    Task RemoveAsync(ContentType type, string hostKey, string folderName, CancellationToken cancellationToken = default);
}

/// <param name="ListingId">The id that the host returned.</param>
/// <param name="FolderName">The folder of the item, which the record matches to a row in any letter case.</param>
/// <param name="Page">The public page of the listing, or null when the host named none.</param>
public sealed record SharedContentEntry(ContentType Type, string HostKey, string HostName, string ListingId, string FolderName, string Version, Uri? Page);

public sealed record SharedContentAuthor(IReadOnlyList<string> Authors, string License);
