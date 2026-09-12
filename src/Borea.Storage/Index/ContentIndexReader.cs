using Borea.Core.Index;
using Borea.Core.Paths;

namespace Borea.Storage.Index;

/// <summary>Reads and maps the cached snapshot through the shared envelope validator.</summary>
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

    public Task<ContentIndexSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(_paths.GetIndexPath(), cancellationToken);

    public async Task ValidateAsync(string candidatePath, CancellationToken cancellationToken = default)
    {
        await ReadAsync(candidatePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContentIndexSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Index does not exist at {path}");

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException($"Index file is empty at {path}");

        cancellationToken.ThrowIfCancellationRequested();
        return Map(SnapshotParser.Parse(content, _source, cancellationToken), cancellationToken);
    }

    private static ContentIndexSnapshot Map(IndexValidationResult result, CancellationToken cancellationToken)
    {
        var diagnostics = new List<ContentIndexDiagnostic>();
        var listings = new List<ContentIndexListing>(result.ValidListings.Count);
        foreach (var listing in result.ValidListings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            listings.Add(new ContentIndexListing(
                listing.Id,
                listing.Authored,
                listing.ValidReleases.ToArray(),
                listing.IndexStatus));

            AddMalformed(diagnostics, listing.IndexStatusError, ContentIndexDiagnosticScope.IndexStatus);
            AddUnsupportedStatus(diagnostics, listing.IndexStatus, listing.Id);
            AddMalformed(diagnostics, listing.RejectedReleases, ContentIndexDiagnosticScope.Release);
            AddUnknown(diagnostics, listing.UnknownReleases, ContentIndexDiagnosticScope.Release);
        }

        AddMalformed(diagnostics, result.MalformedListings, ContentIndexDiagnosticScope.Listing);
        AddUnknown(diagnostics, result.UnknownListings, ContentIndexDiagnosticScope.Listing);

        var packs = new List<ContentIndexPack>(result.ValidPacks.Count);
        foreach (var pack in result.ValidPacks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versions = pack.ValidVersions
                .Select(version => new ContentIndexPackVersion(version.Metadata, version.IndexStatus))
                .ToArray();

            packs.Add(new ContentIndexPack(pack.Id, versions, pack.IndexStatus));
            AddMalformed(diagnostics, pack.IndexStatusError, ContentIndexDiagnosticScope.IndexStatus);
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

        return new ContentIndexSnapshot(result.SnapshotVersion, listings, packs, gameVersions, diagnostics);
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
