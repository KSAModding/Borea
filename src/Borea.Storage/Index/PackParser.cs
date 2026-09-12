using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Parses one entry of the index's packs array. A pack that fails
/// only rejects itself; the rest of the snapshot stays usable. Within a
/// pack, one bad version only rejects that version.
/// </summary>
internal static class PackParser
{
    public static ParseOutcome<ParsedPack> Parse(JsonElement element)
    {
        var id = IndexJsonHelpers.TryExtractString(element, "id");

        PackEntryDto pack;
        try
        {
            pack = element.Deserialize<PackEntryDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The pack deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(id, ex.Message));
        }

        var hasVersions = pack.Versions is { Count: > 0 };

        // No versions: only a tombstone marking removal is a legitimate
        // reason for that, mirroring the listing rule.
        if (!hasVersions)
        {
            return pack.IndexStatus is null
                ? ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(pack.Id,
                    "The pack has no versions and no index_status explaining why."))
                : ParseOutcome<ParsedPack>.Valid(new ParsedPack(
                    pack.Id, Array.Empty<PackVersionDto>(), Array.Empty<RejectedIndexEntry>(),
                    Array.Empty<UnknownIndexVersionEntry>(), pack.IndexStatus));
        }

        var validVersions = new List<PackVersionDto>();
        var rejectedVersions = new List<RejectedIndexEntry>();
        var unknownVersions = new List<UnknownIndexVersionEntry>();

        foreach (var versionElement in pack.Versions ?? new List<JsonElement>())
        {
            var outcome = PackVersionParser.Parse(versionElement);
            switch (outcome.Kind)
            {
                case ParseOutcomeKind.Valid:
                    validVersions.Add(outcome.Value!);
                    break;
                case ParseOutcomeKind.Unknown:
                    unknownVersions.Add(outcome.Unknown!);
                    break;
                case ParseOutcomeKind.Malformed:
                    rejectedVersions.Add(outcome.Malformed!);
                    break;
            }
        }

        return ParseOutcome<ParsedPack>.Valid(new ParsedPack(
            pack.Id, validVersions, rejectedVersions, unknownVersions, pack.IndexStatus));
    }
}
