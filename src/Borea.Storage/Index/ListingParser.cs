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
    public static ParseOutcome<ParsedListing> Parse(JsonElement element)
    {
        var id = IndexJsonHelpers.TryExtractString(element, "id");

        ListingEntryDto listing;
        try
        {
            listing = element.Deserialize<ListingEntryDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The listing deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(id, ex.Message));
        }

        var hasReleases = listing.Releases is { Count: > 0 };

        // No authored data and no releases: only a tombstone marking removal
        // is a legitimate reason for that, per RFC 0031.
        if (listing.Authored is null && !hasReleases)
        {
            return listing.IndexStatus is null
                ? ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(listing.Id,
                    "The listing has no authored data, no releases, and no index_status explaining why."))
                : ParseOutcome<ParsedListing>.Valid(new ParsedListing(
                    listing.Id, null, Array.Empty<ReleasesEntryDto>(), Array.Empty<RejectedIndexEntry>(),
                    Array.Empty<UnknownIndexVersionEntry>(), listing.IndexStatus));
        }

        if (listing.Authored is not null)
        {
            if (SpecVersions.IsAboveHighest(listing.Authored.SpecVersion))
                return ParseOutcome<ParsedListing>.NewUnknown(new UnknownIndexVersionEntry(listing.Id, listing.Authored.SpecVersion));

            if (listing.Authored.SpecVersion < 1)
                return ParseOutcome<ParsedListing>.NewMalformed(new RejectedIndexEntry(listing.Id,
                    $"The listing declares spec_version {listing.Authored.SpecVersion}, which is not valid."));
        }

        var validReleases = new List<ReleasesEntryDto>();
        var rejectedReleases = new List<RejectedIndexEntry>();
        var unknownReleases = new List<UnknownIndexVersionEntry>();

        foreach (var releaseElement in listing.Releases ?? new List<JsonElement>())
        {
            var outcome = ReleaseParser.Parse(releaseElement);
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
            listing.Id, listing.Authored, validReleases, rejectedReleases, unknownReleases, listing.IndexStatus));
    }
}
