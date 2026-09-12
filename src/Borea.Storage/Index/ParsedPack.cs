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

    public ParsedPack(
        string id,
        IReadOnlyList<ParsedPackVersion> validVersions,
        IReadOnlyList<RejectedIndexEntry> rejectedVersions,
        IReadOnlyList<UnknownIndexVersionEntry> unknownVersions,
        IndexStatus? indexStatus)
    {
        ModIds.Validate(id, nameof(id));

        Id = id;
        ValidVersions = validVersions ?? throw new ArgumentNullException(nameof(validVersions));
        RejectedVersions = rejectedVersions ?? throw new ArgumentNullException(nameof(rejectedVersions));
        UnknownVersions = unknownVersions ?? throw new ArgumentNullException(nameof(unknownVersions));
        IndexStatus = indexStatus;
    }
}

/// <summary>One mapped pack version and the moderation state attached to that version.</summary>
public sealed record ParsedPackVersion(ModPackMetadata Metadata, IndexStatus? IndexStatus);
