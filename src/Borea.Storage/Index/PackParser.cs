using Borea.Core.Mods;
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
    public static ParseOutcome<ParsedPack> Parse(JsonElement element, string source)
    {
        var id = IndexJsonHelpers.TryExtractString(element, "id");

        if (!ModIds.IsValid(id))
            return ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(id, $"The pack id '{id}' is not a valid content id."));

        PackEntryDto pack;
        try
        {
            pack = element.Deserialize<PackEntryDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The pack deserialized to null.");
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(id, ex.Message));
        }

        if (!ModIds.Equals(id, pack.Id))
            return ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(id, "The pack id changed while the entry was read."));

        try
        {
            var indexStatus = pack.IndexStatus is null ? null : DtoMapper.MapIndexStatus(pack.IndexStatus);
            var hasVersions = pack.Versions is { Count: > 0 };

            if (!hasVersions)
            {
                return indexStatus is null
                    ? ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(pack.Id,
                        "The pack has no versions and no index_status explaining why."))
                    : ParseOutcome<ParsedPack>.Valid(new ParsedPack(
                        pack.Id, Array.Empty<ParsedPackVersion>(), Array.Empty<RejectedIndexEntry>(),
                        Array.Empty<UnknownIndexVersionEntry>(), indexStatus));
            }

            var validVersions = new List<ParsedPackVersion>();
            var rejectedVersions = new List<RejectedIndexEntry>();
            var unknownVersions = new List<UnknownIndexVersionEntry>();
            var duplicateVersions = FindDuplicateVersions(pack.Versions);

            foreach (var versionElement in pack.Versions ?? [])
            {
                var rawVersion = IndexJsonHelpers.TryExtractNestedString(versionElement, "authored", "version");
                if (ModVersion.TryParse(rawVersion, out var parsedVersion) && duplicateVersions.Contains(parsedVersion))
                {
                    rejectedVersions.Add(new RejectedIndexEntry(
                        pack.Id,
                        rawVersion,
                        $"Pack version '{rawVersion}' appears more than once for pack '{pack.Id}'."));
                    continue;
                }

                var outcome = PackVersionParser.Parse(versionElement, pack.Id, source);
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
                pack.Id, validVersions, rejectedVersions, unknownVersions, indexStatus));
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(pack.Id, ex.Message));
        }
    }

    private static HashSet<ModVersion> FindDuplicateVersions(IReadOnlyList<JsonElement>? versions)
    {
        if (versions is null)
            return [];

        return versions
            .Select(version => IndexJsonHelpers.TryExtractNestedString(version, "authored", "version"))
            .Select(version => ModVersion.TryParse(version, out var parsed) ? (ModVersion?)parsed : null)
            .Where(version => version is not null)
            .Select(version => version!.Value)
            .GroupBy(version => version)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
    }
}
