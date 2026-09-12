using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Deserializes a content index snapshot and sorts every listing and pack
/// into valid, unknown, or malformed.
/// </summary>
public static class SnapshotParser
{
    public static IndexValidationResult Parse(string indexJson, string source = "index")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        SnapshotDto snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SnapshotDto>(indexJson, IndexJsonOptions.Value)
                ?? throw new JsonException("The index deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The index does not match the expected shape. {ex.Message}", ex);
        }

        // Has its own copy for tests.
        if (SnapshotVersions.IsAboveHighest(snapshot.SnapshotVersion))
        {
            throw new InvalidOperationException(
                $"The index is snapshot version {snapshot.SnapshotVersion} and this build reads {SnapshotVersions.Highest}.");
        }

        var validListings = new List<ParsedListing>();
        var unknownListings = new List<UnknownIndexVersionEntry>();
        var malformedListings = new List<RejectedIndexEntry>();
        var duplicateIds = FindDuplicateIds(snapshot.Listings.Concat(snapshot.Packs));

        foreach (var listingElement in snapshot.Listings)
        {
            var id = IndexJsonHelpers.TryExtractString(listingElement, "id");
            if (id is not null && duplicateIds.Contains(id))
            {
                malformedListings.Add(new RejectedIndexEntry(id, $"Content id '{id}' appears more than once in the snapshot."));
                continue;
            }

            var outcome = ListingParser.Parse(listingElement, source);
            switch (outcome.Kind)
            {
                case ParseOutcomeKind.Valid:
                    validListings.Add(outcome.Value!);
                    break;
                case ParseOutcomeKind.Unknown:
                    unknownListings.Add(outcome.Unknown!);
                    break;
                case ParseOutcomeKind.Malformed:
                    malformedListings.Add(outcome.Malformed!);
                    break;
            }
        }

        var validPacks = new List<ParsedPack>();
        var unknownPacks = new List<UnknownIndexVersionEntry>();
        var malformedPacks = new List<RejectedIndexEntry>();

        foreach (var packElement in snapshot.Packs)
        {
            var id = IndexJsonHelpers.TryExtractString(packElement, "id");
            if (id is not null && duplicateIds.Contains(id))
            {
                malformedPacks.Add(new RejectedIndexEntry(id, $"Content id '{id}' appears more than once in the snapshot."));
                continue;
            }

            var outcome = PackParser.Parse(packElement, source);
            switch (outcome.Kind)
            {
                case ParseOutcomeKind.Valid:
                    validPacks.Add(outcome.Value!);
                    break;
                case ParseOutcomeKind.Unknown:
                    unknownPacks.Add(outcome.Unknown!);
                    break;
                case ParseOutcomeKind.Malformed:
                    malformedPacks.Add(outcome.Malformed!);
                    break;
            }
        }

        return new IndexValidationResult(
            snapshot.SnapshotVersion, validListings, unknownListings,
            malformedListings, validPacks, unknownPacks, malformedPacks,
            snapshot.GameVersions, snapshot.Sources);
    }

    private static HashSet<string> FindDuplicateIds(IEnumerable<JsonElement> entries) =>
        entries
            .Select(entry => IndexJsonHelpers.TryExtractString(entry, "id"))
            .Where(ModIds.IsValid)
            .Select(id => id!)
            .GroupBy(id => id, ModIds.Comparer)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(ModIds.Comparer);
}
