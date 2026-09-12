using Borea.Core.Index;
using Borea.Core.Game;
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
    public static IndexValidationResult Parse(
        string indexJson,
        string source = "index",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        cancellationToken.ThrowIfCancellationRequested();
        ContentIndexRootValidator.ValidateIndexRoot(indexJson, "content index");

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

        cancellationToken.ThrowIfCancellationRequested();

        var validListings = new List<ParsedListing>();
        var unknownListings = new List<UnknownIndexVersionEntry>();
        var malformedListings = new List<RejectedIndexEntry>();
        var duplicateIds = FindDuplicateIds(snapshot.Listings.Concat(snapshot.Packs), cancellationToken);

        foreach (var listingElement in snapshot.Listings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = IndexJsonHelpers.TryExtractString(listingElement, "id");
            if (id is not null && duplicateIds.Contains(id))
            {
                malformedListings.Add(new RejectedIndexEntry(id, $"Content id '{id}' appears more than once in the snapshot."));
                continue;
            }

            var outcome = ListingParser.Parse(listingElement, source, cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();
            var id = IndexJsonHelpers.TryExtractString(packElement, "id");
            if (id is not null && duplicateIds.Contains(id))
            {
                malformedPacks.Add(new RejectedIndexEntry(id, $"Content id '{id}' appears more than once in the snapshot."));
                continue;
            }

            var outcome = PackParser.Parse(packElement, source, cancellationToken);
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

        cancellationToken.ThrowIfCancellationRequested();
        var (gameVersions, gameVersionsError) = ParseGameVersions(snapshot.GameVersions, cancellationToken);

        return new IndexValidationResult(
            snapshot.SnapshotVersion, validListings, unknownListings,
            malformedListings, validPacks, unknownPacks, malformedPacks,
            gameVersions, snapshot.Sources, gameVersionsError);
    }

    private static (GameVersionsDto? Value, RejectedIndexEntry? Error) ParseGameVersions(
        JsonElement element,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = element.Deserialize<GameVersionsDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The game_versions value deserialized to null.");

            if (value.SpecVersion != 1)
                throw new FormatException($"The game_versions value uses unsupported spec_version {value.SpecVersion}.");

            if (string.IsNullOrWhiteSpace(value.Source))
                throw new FormatException("The game_versions source is empty.");

            int? previousRevision = null;
            for (var index = 0; index < value.Versions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var raw = value.Versions[index];
                if (!GameVersion.TryParse(raw, out var version))
                    throw new FormatException($"The game_versions item at index {index} is not a valid game version.");

                if (previousRevision is not null && version.Revision <= previousRevision)
                    throw new FormatException("The game_versions items are not in ascending revision order.");

                previousRevision = version.Revision;
            }

            return (value, null);
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return (null, new RejectedIndexEntry(null, $"The game_versions value is unreadable. {ex.Message}"));
        }
    }

    private static HashSet<string> FindDuplicateIds(
        IEnumerable<JsonElement> entries,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(ModIds.Comparer);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = IndexJsonHelpers.TryExtractString(entry, "id");
            if (ModIds.IsValid(id))
                counts[id!] = counts.GetValueOrDefault(id!) + 1;
        }

        return counts
            .Where(pair => pair.Value > 1)
            .Select(pair => pair.Key)
            .ToHashSet(ModIds.Comparer);
    }
}
