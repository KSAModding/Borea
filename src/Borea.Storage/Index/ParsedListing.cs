using Borea.Storage.Index.Dtos;

namespace Borea.Storage.Index;

/// <summary>
/// One successfully-read listing entry: the outer id, its authored data
/// (null for a tombstone), and its releases split into what parsed and what
/// did not.
/// </summary>
public sealed class ParsedListing
{
    public string Id { get; }

    /// <summary>Null for a tombstone: a listing whose only content is an <see cref="IndexStatus"/>.</summary>
    public AuthoredDto? Authored { get; }

    public IReadOnlyList<ReleasesEntryDto> ValidReleases { get; }

    public IReadOnlyList<RejectedIndexEntry> RejectedReleases { get; }

    public IReadOnlyList<UnknownIndexVersionEntry> UnknownReleases { get; }

    /// <summary>Present when the listing is deprecated, removed, or otherwise flagged.</summary>
    public IndexStatusDto? IndexStatus { get; }

    public ParsedListing(
        string id,
        AuthoredDto? authored,
        IReadOnlyList<ReleasesEntryDto> validReleases,
        IReadOnlyList<RejectedIndexEntry> rejectedReleases,
        IReadOnlyList<UnknownIndexVersionEntry> unknownReleases,
        IndexStatusDto? indexStatus)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id cannot be null or whitespace.", nameof(id));

        Id = id;
        Authored = authored;
        ValidReleases = validReleases ?? throw new ArgumentNullException(nameof(validReleases));
        RejectedReleases = rejectedReleases ?? throw new ArgumentNullException(nameof(rejectedReleases));
        UnknownReleases = unknownReleases ?? throw new ArgumentNullException(nameof(unknownReleases));
        IndexStatus = indexStatus;
    }
}
