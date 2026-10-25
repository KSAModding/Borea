using Borea.Core.Index;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Tags;
using Borea.Storage.Index.Dtos;
using System.Text.Json;
using MetadataEnumMapper = Borea.Storage.Mods.MetadataEnumMapper;

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
        CancellationToken cancellationToken = default) =>
        Parse(indexJson, [], source, cancellationToken);

    /// <summary>
    /// Parses the main document together with the type files that sit next to it. A type
    /// file that fails its root check adds one diagnostic and no entries, and leaves the
    /// main document and the other type files usable.
    /// </summary>
    public static IndexValidationResult Parse(
        string indexJson,
        IReadOnlyList<(ContentIndexTypeFile File, string Json)> typeFiles,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeFiles);
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

        var unknownListings = new List<UnknownIndexVersionEntry>();
        var malformedListings = new List<RejectedIndexEntry>();
        var typeEntries = new List<(ContentIndexTypeFile File, IReadOnlyList<JsonElement> Entries)>();
        foreach (var (file, json) in typeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                typeEntries.Add((file, ReadTypeFile(file, json)));
            }
            catch (InvalidOperationException ex)
            {
                malformedListings.Add(new RejectedIndexEntry(null, ex.Message));
            }
        }

        // The ids of every type are one namespace, so an id in two places is rejected in both.
        var duplicateIds = FindDuplicateIds(
            snapshot.Listings.Concat(snapshot.Packs).Concat(typeEntries.SelectMany(entry => entry.Entries)),
            cancellationToken);

        var validListings = ParseListings(snapshot.Listings, null, duplicateIds, source, unknownListings, malformedListings, cancellationToken);
        var validVehicles = new List<ParsedListing>();
        var validSaves = new List<ParsedListing>();
        foreach (var (file, entries) in typeEntries)
        {
            var valid = ParseListings(entries, file, duplicateIds, source, unknownListings, malformedListings, cancellationToken);
            (file.Type == ContentType.Save ? validSaves : validVehicles).AddRange(valid);
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
        var (tags, tagsError, unknownTags) = ParseTags(snapshot.Tags, cancellationToken);

        return new IndexValidationResult(
            snapshot.SnapshotVersion, validListings, unknownListings,
            malformedListings, validPacks, unknownPacks, malformedPacks,
            gameVersions, snapshot.Sources, gameVersionsError, tags, tagsError, unknownTags,
            validVehicles, validSaves);
    }

    private static List<JsonElement> ReadTypeFile(ContentIndexTypeFile file, string json)
    {
        ContentIndexRootValidator.ValidateTypeFileRoot(json, "content index", file);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(file.ArrayKey).Clone().EnumerateArray().ToList();
    }

    /// <summary>
    /// The one parse of listing entries, for the listings of the main document (<paramref name="file"/> is null)
    /// and for the entries of a type file.
    /// </summary>
    private static List<ParsedListing> ParseListings(
        IReadOnlyList<JsonElement> entries,
        ContentIndexTypeFile? file,
        HashSet<string> duplicateIds,
        string source,
        List<UnknownIndexVersionEntry> unknownListings,
        List<RejectedIndexEntry> malformedListings,
        CancellationToken cancellationToken)
    {
        var validListings = new List<ParsedListing>();
        foreach (var listingElement in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = IndexJsonHelpers.TryExtractString(listingElement, "id");
            if (id is not null && duplicateIds.Contains(id))
            {
                malformedListings.Add(new RejectedIndexEntry(id, $"Content id '{id}' appears more than once in the snapshot."));
                continue;
            }

            if (MisplacedReason(listingElement, id, file) is { } reason)
            {
                malformedListings.Add(new RejectedIndexEntry(id, reason));
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

        return validListings;
    }

    /// <summary>
    /// Why an entry does not belong where it is, read from the raw authored type, or null when it does.
    /// Vehicles and saves have their own files, so a client that knows no such file never lists them.
    /// </summary>
    private static string? MisplacedReason(JsonElement entry, string? id, ContentIndexTypeFile? file)
    {
        var rawType = IndexJsonHelpers.TryExtractNestedString(entry, "authored", "type");
        if (file is null)
        {
            var type = rawType is null ? ContentType.Mod : MetadataEnumMapper.ParseContentType(rawType);
            return ContentIndexTypeFiles.All.FirstOrDefault(typeFile => typeFile.Type == type) is { } home
                ? $"Listing '{id}' is a {rawType}, which belongs in {home.FileName} and not in listings."
                : null;
        }

        var hasAuthored = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("authored", out var authored) && authored.ValueKind != JsonValueKind.Null;
        if (hasAuthored)
        {
            return rawType is not null && MetadataEnumMapper.ParseContentType(rawType) == file.Type
                ? null
                : $"Entry '{id}' in {file.FileName} has type '{rawType}', and that file holds only {MetadataEnumMapper.ToDto(file.Type)} entries.";
        }

        // Without an authored document nothing says what the releases are, and a tombstone has none.
        return entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("releases", out var releases) && releases is { ValueKind: JsonValueKind.Array } && releases.GetArrayLength() > 0
            ? $"Entry '{id}' in {file.FileName} has releases but no authored document."
            : null;
    }

    private static (CuratedTagVocabulary Value, RejectedIndexEntry? Error, UnknownIndexVersionEntry? Unknown) ParseTags(
        JsonElement? element,
        CancellationToken cancellationToken)
    {
        if (element is null || element.Value.ValueKind == JsonValueKind.Null)
            return (CuratedTagVocabulary.Empty, null, null);

        var specVersion = IndexJsonHelpers.TryExtractInt(element.Value, "spec_version");
        if (specVersion is not null && specVersion != 1)
        {
            return (
                CuratedTagVocabulary.Empty,
                null,
                new UnknownIndexVersionEntry(null, null, specVersion.Value, $"The curated tag vocabulary uses unsupported spec_version {specVersion}."));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dto = element.Value.Deserialize<CuratedTagsDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The tags value deserialized to null.");
            var tags = dto.Mod.Select(item => new CuratedTag(item.Tag, item.Name, item.Meaning, item.ForumPrefix, item.ForumPrefixId)).ToArray();
            return (new CuratedTagVocabulary(dto.SpecVersion, tags), null, null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException)
        {
            return (CuratedTagVocabulary.Empty, new RejectedIndexEntry(null, $"The curated tag vocabulary is unreadable. {ex.Message}"), null);
        }
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
