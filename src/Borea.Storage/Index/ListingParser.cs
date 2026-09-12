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
    public static ParseOutcome<ParsedListing> Parse(JsonElement element, string source)
    {
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

        try
        {
            var indexStatus = listing.IndexStatus is null ? null : DtoMapper.MapIndexStatus(listing.IndexStatus);

            if (listing.Authored is null && !hasReleases)
            {
                return indexStatus is null
                    ? ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(listing.Id,
                        "The listing has no authored data, no releases, and no index_status explaining why."))
                    : ParseOutcome<ParsedListing>.Valid(new ParsedListing(
                        listing.Id, null, Array.Empty<ModVersionMetadata>(), Array.Empty<RejectedIndexEntry>(),
                        Array.Empty<UnknownIndexVersionEntry>(), indexStatus));
            }

            var authored = listing.Authored is null ? null : DtoMapper.MapAuthored(listing.Authored, source);

            var validReleases = new List<ModVersionMetadata>();
            var rejectedReleases = new List<RejectedIndexEntry>();
            var unknownReleases = new List<UnknownIndexVersionEntry>();
            var duplicateVersions = FindDuplicateVersions(listing.Releases);

            foreach (var releaseElement in listing.Releases ?? [])
            {
                var rawVersion = IndexJsonHelpers.TryExtractString(releaseElement, "version");
                if (ModVersion.TryParse(rawVersion, out var parsedVersion) && duplicateVersions.Contains(parsedVersion))
                {
                    rejectedReleases.Add(new RejectedIndexEntry(
                        listing.Id,
                        rawVersion,
                        $"Release version '{rawVersion}' appears more than once for listing '{listing.Id}'."));
                    continue;
                }

                var outcome = ReleaseParser.Parse(releaseElement, listing.Id, source, authored);
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
                listing.Id, authored, validReleases, rejectedReleases, unknownReleases, indexStatus));
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(listing.Id, ex.Message));
        }
    }

    private static HashSet<ModVersion> FindDuplicateVersions(IReadOnlyList<JsonElement>? releases)
    {
        if (releases is null)
            return [];

        return releases
            .Select(release => IndexJsonHelpers.TryExtractString(release, "version"))
            .Select(version => ModVersion.TryParse(version, out var parsed) ? (ModVersion?)parsed : null)
            .Where(version => version is not null)
            .Select(version => version!.Value)
            .GroupBy(version => version)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
    }
}
