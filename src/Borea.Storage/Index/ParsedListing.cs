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

    public ParsedListing(
        string id,
        ModMetadata? authored,
        IReadOnlyList<ModVersionMetadata> validReleases,
        IReadOnlyList<RejectedIndexEntry> rejectedReleases,
        IReadOnlyList<UnknownIndexVersionEntry> unknownReleases,
        IndexStatus? indexStatus)
    {
        ModIds.Validate(id, nameof(id));

        Id = id;
        Authored = authored;
        ValidReleases = validReleases ?? throw new ArgumentNullException(nameof(validReleases));
        RejectedReleases = rejectedReleases ?? throw new ArgumentNullException(nameof(rejectedReleases));
        UnknownReleases = unknownReleases ?? throw new ArgumentNullException(nameof(unknownReleases));
        IndexStatus = indexStatus;
    }
}
