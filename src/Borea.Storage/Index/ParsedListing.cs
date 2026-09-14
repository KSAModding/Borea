using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Storage.Index;

/// <summary>
/// One successfully-read listing entry with all supported metadata mapped to Core types.
/// </summary>
public sealed class ParsedListing
{
    public string Id { get; }

    /// <summary>Null for a tombstone: a listing whose only content is an <see cref="IndexStatus"/>.</summary>
    public ModMetadata? Authored { get; }

    public IReadOnlyList<ModVersionMetadata> ValidReleases { get; }

    public IReadOnlyList<RejectedIndexEntry> RejectedReleases { get; }

    public IReadOnlyList<UnknownIndexVersionEntry> UnknownReleases { get; }

    /// <summary>Present when the listing is deprecated, removed, or otherwise flagged.</summary>
    public IndexStatus? IndexStatus { get; }

    /// <summary>Present when index_status exists but cannot be read safely.</summary>
    public RejectedIndexEntry? IndexStatusError { get; }

    /// <summary>Null when the listing carries no downloads value or when that value cannot be read.</summary>
    public ListingDownloadCounts? Downloads { get; }

    /// <summary>The parts of downloads that cannot be read. The listing stays usable.</summary>
    public IReadOnlyList<RejectedIndexEntry> DownloadsErrors { get; }

    /// <summary>Null when the authored document has no images or none of them can be read.</summary>
    public ContentImages? Images { get; }

    /// <summary>The parts of images that cannot be read. The listing stays usable.</summary>
    public IReadOnlyList<RejectedIndexEntry> ImagesErrors { get; }

    public DateTimeOffset? PublishedAt { get; }

    public DateTimeOffset? UpdatedAt { get; }

    /// <summary>The dates that cannot be read. The listing stays usable.</summary>
    public IReadOnlyList<RejectedIndexEntry> DatesErrors { get; }

    public ParsedListing(
        string id,
        ModMetadata? authored,
        IReadOnlyList<ModVersionMetadata> validReleases,
        IReadOnlyList<RejectedIndexEntry> rejectedReleases,
        IReadOnlyList<UnknownIndexVersionEntry> unknownReleases,
        IndexStatus? indexStatus,
        RejectedIndexEntry? indexStatusError = null,
        ListingDownloadCounts? downloads = null,
        IReadOnlyList<RejectedIndexEntry>? downloadsErrors = null,
        ContentImages? images = null,
        IReadOnlyList<RejectedIndexEntry>? imagesErrors = null,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? updatedAt = null,
        IReadOnlyList<RejectedIndexEntry>? datesErrors = null)
    {
        ModIds.Validate(id, nameof(id));

        Id = id;
        Authored = authored;
        ValidReleases = validReleases ?? throw new ArgumentNullException(nameof(validReleases));
        RejectedReleases = rejectedReleases ?? throw new ArgumentNullException(nameof(rejectedReleases));
        UnknownReleases = unknownReleases ?? throw new ArgumentNullException(nameof(unknownReleases));
        IndexStatus = indexStatus;
        IndexStatusError = indexStatusError;
        Downloads = downloads;
        DownloadsErrors = downloadsErrors ?? Array.Empty<RejectedIndexEntry>();
        Images = images;
        ImagesErrors = imagesErrors ?? Array.Empty<RejectedIndexEntry>();
        PublishedAt = publishedAt;
        UpdatedAt = updatedAt;
        DatesErrors = datesErrors ?? Array.Empty<RejectedIndexEntry>();
    }
}
