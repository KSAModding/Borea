using Borea.Core.Index;
using Borea.Core.Paths;

namespace Borea.Storage.Index;

/// <summary>
/// Reads and maps the cached snapshot through the shared envelope validator, with the type
/// files of <see cref="ContentIndexTypeFiles"/> next to it. A type file that is not there is empty.
/// </summary>
public sealed class ContentIndexReader : IContentIndexReader, IContentIndexCandidateValidator
{
    private readonly IGamePathProvider _paths;
    private readonly string _source;

    public ContentIndexReader(IGamePathProvider paths, string source = "index")
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        _source = source;
    }

    public async Task<ContentIndexSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.GetIndexPath();
        var content = await ReadIndexAsync(path, cancellationToken).ConfigureAwait(false);
        var typeFiles = new List<(ContentIndexTypeFile File, string Json)>();
        var diagnostics = new List<ContentIndexDiagnostic>();
        foreach (var file in ContentIndexTypeFiles.All)
        {
            var typePath = file.CachePathFrom(path);
            try
            {
                if (File.Exists(typePath))
                    typeFiles.Add((file, await File.ReadAllTextAsync(typePath, cancellationToken).ConfigureAwait(false)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new ContentIndexDiagnostic(
                    ContentIndexDiagnosticKind.Malformed,
                    ContentIndexDiagnosticScope.Listing,
                    $"{file.FileName} cannot be read. {ex.Message}"));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Map(SnapshotParser.Parse(content, typeFiles, _source, cancellationToken), diagnostics, cancellationToken);
    }

    /// <summary>Checks the main document only, because a type file never fails the main refresh.</summary>
    public async Task ValidateAsync(string candidatePath, CancellationToken cancellationToken = default)
    {
        var content = await ReadIndexAsync(candidatePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Map(SnapshotParser.Parse(content, _source, cancellationToken), [], cancellationToken);
    }

    private static async Task<string> ReadIndexAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Index does not exist at {path}");

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException($"Index file is empty at {path}");

        return content;
    }

    private static ContentIndexSnapshot Map(IndexValidationResult result, List<ContentIndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var listings = MapListings(result.ValidListings, diagnostics, cancellationToken);
        var vehicles = MapListings(result.ValidVehicles, diagnostics, cancellationToken);
        var saves = MapListings(result.ValidSaves, diagnostics, cancellationToken);

        AddMalformed(diagnostics, result.MalformedListings, ContentIndexDiagnosticScope.Listing);
        AddUnknown(diagnostics, result.UnknownListings, ContentIndexDiagnosticScope.Listing);

        var packs = new List<ContentIndexPack>(result.ValidPacks.Count);
        foreach (var pack in result.ValidPacks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versions = pack.ValidVersions
                .Select(version => new ContentIndexPackVersion(version.Metadata, version.IndexStatus, version.Images) { VersionText = version.VersionText })
                .ToArray();

            packs.Add(new ContentIndexPack(pack.Id, versions, pack.IndexStatus, pack.PublishedAt, pack.UpdatedAt, pack.ListedAt));
            AddMalformed(diagnostics, pack.IndexStatusError, ContentIndexDiagnosticScope.IndexStatus);
            AddMalformed(diagnostics, pack.DatesErrors, ContentIndexDiagnosticScope.Dates);
            AddUnsupportedStatus(diagnostics, pack.IndexStatus, pack.Id);
            foreach (var version in pack.ValidVersions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMalformed(diagnostics, version.IndexStatusError, ContentIndexDiagnosticScope.IndexStatus);
                AddUnsupportedStatus(
                    diagnostics,
                    version.IndexStatus,
                    pack.Id,
                    version.Metadata.Version.ToString());
                AddMalformed(diagnostics, version.ImagesErrors, ContentIndexDiagnosticScope.Images);
            }
            AddMalformed(diagnostics, pack.RejectedVersions, ContentIndexDiagnosticScope.PackVersion);
            AddUnknown(diagnostics, pack.UnknownVersions, ContentIndexDiagnosticScope.PackVersion);
        }

        AddMalformed(diagnostics, result.MalformedPacks, ContentIndexDiagnosticScope.Pack);
        AddUnknown(diagnostics, result.UnknownPacks, ContentIndexDiagnosticScope.Pack);
        AddMalformed(diagnostics, result.GameVersionsError, ContentIndexDiagnosticScope.GameVersions);

        var gameVersions = result.GameVersions is null
            ? null
            : new ContentIndexGameVersions(
                result.GameVersions.SpecVersion,
                result.GameVersions.Source,
                result.GameVersions.Versions.ToArray());

        AddMalformed(diagnostics, result.TagsError, ContentIndexDiagnosticScope.Tags);
        if (result.UnknownTags is not null)
        {
            diagnostics.Add(new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                ContentIndexDiagnosticScope.Tags,
                result.UnknownTags.Reason,
                SpecVersion: result.UnknownTags.SpecVersion));
        }

        return new ContentIndexSnapshot(result.SnapshotVersion, listings, packs, gameVersions, diagnostics, result.Tags, vehicles, saves);
    }

    private static List<ContentIndexListing> MapListings(
        IReadOnlyList<ParsedListing> parsed,
        List<ContentIndexDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var listings = new List<ContentIndexListing>(parsed.Count);
        foreach (var listing in parsed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            listings.Add(new ContentIndexListing(
                listing.Id,
                listing.Authored,
                listing.ValidReleases.ToArray(),
                listing.IndexStatus,
                listing.Downloads,
                listing.Images,
                listing.PublishedAt,
                listing.UpdatedAt,
                listing.ListedAt));

            AddMalformed(diagnostics, listing.IndexStatusError, ContentIndexDiagnosticScope.IndexStatus);
            AddUnsupportedStatus(diagnostics, listing.IndexStatus, listing.Id);
            AddMalformed(diagnostics, listing.DownloadsErrors, ContentIndexDiagnosticScope.Downloads);
            AddMalformed(diagnostics, listing.ImagesErrors, ContentIndexDiagnosticScope.Images);
            AddMalformed(diagnostics, listing.DatesErrors, ContentIndexDiagnosticScope.Dates);
            AddMalformed(diagnostics, listing.ChangelogTextErrors, ContentIndexDiagnosticScope.ChangelogText);
            AddMalformed(diagnostics, listing.RejectedReleases, ContentIndexDiagnosticScope.Release);
            AddUnknown(diagnostics, listing.UnknownReleases, ContentIndexDiagnosticScope.Release);
        }

        return listings;
    }

    private static void AddUnsupportedStatus(
        ICollection<ContentIndexDiagnostic> diagnostics,
        IndexStatus? status,
        string id,
        string? version = null)
    {
        if (status?.State == IndexStatusState.Unknown)
        {
            diagnostics.Add(new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedValue,
                ContentIndexDiagnosticScope.IndexStatus,
                $"Index status state '{status.RawState}' is not supported.",
                id,
                version));
        }
    }

    private static void AddMalformed(
        ICollection<ContentIndexDiagnostic> diagnostics,
        RejectedIndexEntry? entry,
        ContentIndexDiagnosticScope scope)
    {
        if (entry is not null)
            diagnostics.Add(new ContentIndexDiagnostic(ContentIndexDiagnosticKind.Malformed, scope, entry.Reason, entry.Id, entry.Version));
    }

    private static void AddMalformed(
        ICollection<ContentIndexDiagnostic> diagnostics,
        IEnumerable<RejectedIndexEntry> entries,
        ContentIndexDiagnosticScope scope)
    {
        foreach (var entry in entries)
            AddMalformed(diagnostics, entry, scope);
    }

    private static void AddUnknown(
        ICollection<ContentIndexDiagnostic> diagnostics,
        IEnumerable<UnknownIndexVersionEntry> entries,
        ContentIndexDiagnosticScope scope)
    {
        foreach (var entry in entries)
        {
            diagnostics.Add(new ContentIndexDiagnostic(
                ContentIndexDiagnosticKind.UnsupportedVersion,
                scope,
                entry.Reason,
                entry.Id,
                entry.Version,
                entry.SpecVersion));
        }
    }
}
