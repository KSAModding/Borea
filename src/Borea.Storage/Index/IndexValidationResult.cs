using Borea.Storage.Index.Dtos;

namespace Borea.Storage.Index;

/// <summary>
/// The outcome of parsing a whole content index snapshot: every listing and
/// pack sorted into valid, unknown, or malformed.
/// </summary>
public sealed class IndexValidationResult
{
    public int SnapshotVersion { get; }
    public IReadOnlyList<ParsedListing> ValidListings { get; }
    public IReadOnlyList<UnknownIndexVersionEntry> UnknownListings { get; }
    public IReadOnlyList<RejectedIndexEntry> MalformedListings { get; }

    public IReadOnlyList<ParsedPack> ValidPacks { get; }
    public IReadOnlyList<UnknownIndexVersionEntry> UnknownPacks { get; }
    public IReadOnlyList<RejectedIndexEntry> MalformedPacks { get; }

    public GameVersionsDto? GameVersions { get; }

    public RejectedIndexEntry? GameVersionsError { get; }

    /// <summary>Null when the index does not carry a sources table.</summary>
    public SourcesDto? Sources { get; }

    public IndexValidationResult(
        int snapshotVersion,
        IReadOnlyList<ParsedListing> validListings,
        IReadOnlyList<UnknownIndexVersionEntry> unknownListings,
        IReadOnlyList<RejectedIndexEntry> malformedListings,
        IReadOnlyList<ParsedPack> validPacks,
        IReadOnlyList<UnknownIndexVersionEntry> unknownPacks,
        IReadOnlyList<RejectedIndexEntry> malformedPacks,
        GameVersionsDto? gameVersions,
        SourcesDto? sources,
        RejectedIndexEntry? gameVersionsError = null)
    {
        SnapshotVersion = snapshotVersion;
        ValidListings = validListings ?? throw new ArgumentNullException(nameof(validListings));
        UnknownListings = unknownListings ?? throw new ArgumentNullException(nameof(unknownListings));
        MalformedListings = malformedListings ?? throw new ArgumentNullException(nameof(malformedListings));
        ValidPacks = validPacks ?? throw new ArgumentNullException(nameof(validPacks));
        UnknownPacks = unknownPacks ?? throw new ArgumentNullException(nameof(unknownPacks));
        MalformedPacks = malformedPacks ?? throw new ArgumentNullException(nameof(malformedPacks));
        GameVersions = gameVersions;
        Sources = sources;
        GameVersionsError = gameVersionsError;
    }
}
