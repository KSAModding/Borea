using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Storage.Index;

/// <summary>
/// One successfully-read pack entry with all supported versions mapped to Core types.
/// </summary>
public sealed class ParsedPack
{
    public string Id { get; }

    public IReadOnlyList<ParsedPackVersion> ValidVersions { get; }

    public IReadOnlyList<RejectedIndexEntry> RejectedVersions { get; }

    public IReadOnlyList<UnknownIndexVersionEntry> UnknownVersions { get; }

    /// <summary>Present when the pack is deprecated, removed, or otherwise flagged.</summary>
    public IndexStatus? IndexStatus { get; }

    /// <summary>Present when index_status exists but cannot be read safely.</summary>
    public RejectedIndexEntry? IndexStatusError { get; }

    public DateTimeOffset? PublishedAt { get; }

    public DateTimeOffset? UpdatedAt { get; }

    /// <summary>The dates that cannot be read. The pack stays usable.</summary>
    public IReadOnlyList<RejectedIndexEntry> DatesErrors { get; }

    public ParsedPack(
        string id,
        IReadOnlyList<ParsedPackVersion> validVersions,
        IReadOnlyList<RejectedIndexEntry> rejectedVersions,
        IReadOnlyList<UnknownIndexVersionEntry> unknownVersions,
        IndexStatus? indexStatus,
        RejectedIndexEntry? indexStatusError = null,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? updatedAt = null,
        IReadOnlyList<RejectedIndexEntry>? datesErrors = null)
    {
        ModIds.Validate(id, nameof(id));

        Id = id;
        ValidVersions = validVersions ?? throw new ArgumentNullException(nameof(validVersions));
        RejectedVersions = rejectedVersions ?? throw new ArgumentNullException(nameof(rejectedVersions));
        UnknownVersions = unknownVersions ?? throw new ArgumentNullException(nameof(unknownVersions));
        IndexStatus = indexStatus;
        IndexStatusError = indexStatusError;
        PublishedAt = publishedAt;
        UpdatedAt = updatedAt;
        DatesErrors = datesErrors ?? Array.Empty<RejectedIndexEntry>();
    }
}

/// <summary>One mapped pack version, the moderation state attached to that version, and the images of its document.</summary>
public sealed record ParsedPackVersion(
    ModPackMetadata Metadata,
    IndexStatus? IndexStatus,
    RejectedIndexEntry? IndexStatusError = null,
    ContentImages? Images = null)
{
    public IReadOnlyList<RejectedIndexEntry> ImagesErrors { get; init; } = Array.Empty<RejectedIndexEntry>();
}
