using Borea.Core.Mods;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Parses one entry of the index's listings array. A listing that
/// fails only rejects itself; the rest of the snapshot stays usable.
/// </summary>
public static class ListingParser
{
    public static ParseOutcome<ParsedListing> Parse(
        JsonElement element,
        string source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = IndexJsonHelpers.TryExtractString(element, "id");

        if (!ModIds.IsValid(id))
            return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(id, $"The listing id '{id}' is not a valid content id."));

        var authoredSpecVersion = IndexJsonHelpers.TryExtractNestedInt(element, "authored", "spec_version");
        if (authoredSpecVersion is { } rawSpecVersion)
        {
            if (SpecVersions.IsAboveHighest(rawSpecVersion))
            {
                return ParseOutcome<ParsedListing>.NewUnknown(new UnknownIndexVersionEntry(
                    id,
                    null,
                    rawSpecVersion,
                    $"Listing '{id}' uses unsupported spec_version {rawSpecVersion}."));
            }

            if (rawSpecVersion < 1)
                return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(id, $"The listing declares spec_version {rawSpecVersion}, which is not valid."));
        }

        ListingEntryDto listing;
        try
        {
            listing = element.Deserialize<ListingEntryDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The listing deserialized to null.");
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(id, ex.Message));
        }

        if (!ModIds.Equals(id, listing.Id))
            return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(id, "The listing id changed while the entry was read."));

        if (listing.Authored is not null && !ModIds.Equals(listing.Id, listing.Authored.Id))
        {
            return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(
                listing.Id,
                $"The outer id '{listing.Id}' does not agree with authored id '{listing.Authored.Id}'."));
        }

        var hasReleases = listing.Releases is { Count: > 0 };
        var (indexStatus, indexStatusError) = IndexStatusParser.Parse(element, listing.Id);

        try
        {
            if (listing.Authored is null && !hasReleases)
            {
                return indexStatus is null && indexStatusError is null
                    ? ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(listing.Id,
                        "The listing has no authored data, no releases, and no index_status explaining why."))
                    : ParseOutcome<ParsedListing>.Valid(new ParsedListing(
                        listing.Id, null, Array.Empty<ModVersionMetadata>(), Array.Empty<RejectedIndexEntry>(),
                        Array.Empty<UnknownIndexVersionEntry>(), indexStatus, indexStatusError));
            }

            var authored = listing.Authored is null ? null : DtoMapper.MapAuthored(listing.Authored, source);

            var validReleases = new List<ModVersionMetadata>();
            var rejectedReleases = new List<RejectedIndexEntry>();
            var unknownReleases = new List<UnknownIndexVersionEntry>();
            var duplicateVersions = FindDuplicateVersions(listing.Releases, cancellationToken);

            foreach (var releaseElement in listing.Releases ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rawVersion = IndexJsonHelpers.TryExtractString(releaseElement, "version");
                if (ModVersion.TryParse(rawVersion, out var parsedVersion) && duplicateVersions.Contains(parsedVersion))
                {
                    rejectedReleases.Add(new RejectedIndexEntry(
                        listing.Id,
                        rawVersion,
                        $"Release version '{rawVersion}' appears more than once for listing '{listing.Id}'."));
                    continue;
                }

                var outcome = ReleaseParser.Parse(releaseElement, listing.Id, source, authored, cancellationToken);
                switch (outcome.Kind)
                {
                    case ParseOutcomeKind.Valid:
                        validReleases.Add(outcome.Value!);
                        break;
                    case ParseOutcomeKind.Unknown:
                        unknownReleases.Add(outcome.Unknown!);
                        break;
                    case ParseOutcomeKind.Malformed:
                        rejectedReleases.Add(outcome.Malformed!);
                        break;
                }
            }

            return ParseOutcome<ParsedListing>.Valid(new ParsedListing(
                listing.Id, authored, validReleases, rejectedReleases, unknownReleases, indexStatus, indexStatusError));
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(listing.Id, ex.Message));
        }
    }

    private static HashSet<ModVersion> FindDuplicateVersions(
        IReadOnlyList<JsonElement>? releases,
        CancellationToken cancellationToken)
    {
        if (releases is null)
            return [];

        var counts = new Dictionary<ModVersion, int>();
        foreach (var release in releases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawVersion = IndexJsonHelpers.TryExtractString(release, "version");
            if (ModVersion.TryParse(rawVersion, out var version))
                counts[version] = counts.GetValueOrDefault(version) + 1;
        }

        return counts.Where(pair => pair.Value > 1).Select(pair => pair.Key).ToHashSet();
    }
}
