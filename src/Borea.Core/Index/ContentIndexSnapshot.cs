using System.Collections.ObjectModel;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Tags;

namespace Borea.Core.Index;

/// <summary>The supported content and diagnostics read from one snapshot.</summary>
public sealed class ContentIndexSnapshot
{
    public int SnapshotVersion { get; }

    public IReadOnlyList<ContentIndexListing> Listings { get; }

    public IReadOnlyList<ContentIndexPack> Packs { get; }

    public ContentIndexGameVersions? GameVersions { get; }

    public IReadOnlyList<ContentIndexDiagnostic> Diagnostics { get; }

    public CuratedTagVocabulary Tags { get; }

    public ContentIndexSnapshot(
        int snapshotVersion,
        IReadOnlyList<ContentIndexListing> listings,
        IReadOnlyList<ContentIndexPack> packs,
        ContentIndexGameVersions? gameVersions,
        IReadOnlyList<ContentIndexDiagnostic> diagnostics,
        CuratedTagVocabulary? tags = null)
    {
        if (snapshotVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(snapshotVersion), "Snapshot version must be a positive integer.");

        SnapshotVersion = snapshotVersion;
        Listings = Copy(listings, nameof(listings));
        Packs = Copy(packs, nameof(packs));
        GameVersions = gameVersions;
        Diagnostics = Copy(diagnostics, nameof(diagnostics));
        Tags = tags ?? CuratedTagVocabulary.Empty;
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}

public sealed class ContentIndexListing
{
    public string Id { get; }

    public ModMetadata? Authored { get; }

    public IReadOnlyList<ModVersionMetadata> Releases { get; }

    public IndexStatus? IndexStatus { get; }

    /// <summary>Null when the index reports no download counts for this listing, which means unknown.</summary>
    public ListingDownloadCounts? Downloads { get; }

    /// <summary>Null when the authored document has no usable images.</summary>
    public ContentImages? Images { get; }

    /// <summary>The earliest release date the index reports, yanked releases included.</summary>
    public DateTimeOffset? PublishedAt { get; }

    /// <summary>The latest release date the index reports for a release that is not yanked.</summary>
    public DateTimeOffset? UpdatedAt { get; }

    public ContentIndexListing(
        string id,
        ModMetadata? authored,
        IReadOnlyList<ModVersionMetadata> releases,
        IndexStatus? indexStatus,
        ListingDownloadCounts? downloads = null,
        ContentImages? images = null,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? updatedAt = null)
    {
        ContentIndexDates.Validate(publishedAt, updatedAt);
        ModIds.Validate(id, nameof(id));
        if (authored is not null && !ModIds.Equals(id, authored.ModId))
            throw new ArgumentException("The authored listing id must match the outer id.", nameof(authored));

        var releaseCopy = Copy(releases, nameof(releases));
        if (releaseCopy.Any(release => !ModIds.Equals(id, release.ModId)))
            throw new ArgumentException("Each release id must match the outer id.", nameof(releases));

        Id = id;
        Authored = authored;
        Releases = releaseCopy;
        IndexStatus = indexStatus;
        Downloads = downloads;
        Images = images;
        PublishedAt = publishedAt;
        UpdatedAt = updatedAt;
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}

public sealed class ContentIndexPack
{
    public string Id { get; }

    public IReadOnlyList<ContentIndexPackVersion> Versions { get; }

    public IndexStatus? IndexStatus { get; }

    /// <summary>The earliest release date the index reports, retracted versions included.</summary>
    public DateTimeOffset? PublishedAt { get; }

    /// <summary>The latest release date the index reports for a version that is not retracted.</summary>
    public DateTimeOffset? UpdatedAt { get; }

    public ContentIndexPack(
        string id,
        IReadOnlyList<ContentIndexPackVersion> versions,
        IndexStatus? indexStatus,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? updatedAt = null)
    {
        ModIds.Validate(id, nameof(id));
        ContentIndexDates.Validate(publishedAt, updatedAt);
        var versionCopy = Copy(versions, nameof(versions));
        if (versionCopy.Any(version => !ModIds.Equals(id, version.Metadata.ModPackId)))
            throw new ArgumentException("Each pack version id must match the outer id.", nameof(versions));

        Id = id;
        Versions = versionCopy;
        IndexStatus = indexStatus;
        PublishedAt = publishedAt;
        UpdatedAt = updatedAt;
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}

/// <summary>One pack version. <see cref="Images"/> is null when the version document has no usable images.</summary>
public sealed record ContentIndexPackVersion(ModPackMetadata Metadata, IndexStatus? IndexStatus, ContentImages? Images = null);

internal static class ContentIndexDates
{
    public static void Validate(DateTimeOffset? publishedAt, DateTimeOffset? updatedAt)
    {
        if (publishedAt is { } published && updatedAt is { } updated && updated < published)
            throw new ArgumentException($"The updated date {updated:O} cannot be before the published date {published:O}.", nameof(updatedAt));
    }
}

public sealed class ContentIndexGameVersions
{
    public int SpecVersion { get; }

    public string Source { get; }

    public IReadOnlyList<string> Versions { get; }

    public ContentIndexGameVersions(int specVersion, string source, IReadOnlyList<string> versions)
    {
        if (specVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(specVersion), "Spec version must be a positive integer.");

        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(versions);

        SpecVersion = specVersion;
        Source = source;
        Versions = new ReadOnlyCollection<string>(versions.ToArray());
    }
}
