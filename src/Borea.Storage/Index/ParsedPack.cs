using Borea.Storage.Index.Dtos;

namespace Borea.Storage.Index;

/// <summary>
/// One successfully-read pack entry: the outer id and its versions split
/// into what parsed and what did not.
/// </summary>
public sealed class ParsedPack
{
    public string Id { get; }

    public IReadOnlyList<PackVersionDto> ValidVersions { get; }

    public IReadOnlyList<RejectedIndexEntry> RejectedVersions { get; }

    public IReadOnlyList<UnknownIndexVersionEntry> UnknownVersions { get; }

    /// <summary>Present when the pack is deprecated, removed, or otherwise flagged.</summary>
    public IndexStatusDto? IndexStatus { get; }

    public ParsedPack(
        string id,
        IReadOnlyList<PackVersionDto> validVersions,
        IReadOnlyList<RejectedIndexEntry> rejectedVersions,
        IReadOnlyList<UnknownIndexVersionEntry> unknownVersions,
        IndexStatusDto? indexStatus)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id cannot be null or whitespace.", nameof(id));

        Id = id;
        ValidVersions = validVersions ?? throw new ArgumentNullException(nameof(validVersions));
        RejectedVersions = rejectedVersions ?? throw new ArgumentNullException(nameof(rejectedVersions));
        UnknownVersions = unknownVersions ?? throw new ArgumentNullException(nameof(unknownVersions));
        IndexStatus = indexStatus;
    }
}
