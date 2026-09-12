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
    public static ParseOutcome<ParsedPack> Parse(
        JsonElement element,
        string source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        var (indexStatus, indexStatusError) = IndexStatusParser.Parse(element, pack.Id);

        try
        {
            var hasVersions = pack.Versions is { Count: > 0 };

            if (!hasVersions)
            {
                return indexStatus is null && indexStatusError is null
                    ? ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(pack.Id,
                        "The pack has no versions and no index_status explaining why."))
                    : ParseOutcome<ParsedPack>.Valid(new ParsedPack(
                        pack.Id, Array.Empty<ParsedPackVersion>(), Array.Empty<RejectedIndexEntry>(),
                        Array.Empty<UnknownIndexVersionEntry>(), indexStatus, indexStatusError));
            }

            var validVersions = new List<ParsedPackVersion>();
            var rejectedVersions = new List<RejectedIndexEntry>();
            var unknownVersions = new List<UnknownIndexVersionEntry>();
            var duplicateVersions = FindDuplicateVersions(pack.Versions, cancellationToken);

            foreach (var versionElement in pack.Versions ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rawVersion = IndexJsonHelpers.TryExtractNestedString(versionElement, "authored", "version");
                if (ModVersion.TryParse(rawVersion, out var parsedVersion) && duplicateVersions.Contains(parsedVersion))
                {
                    rejectedVersions.Add(new RejectedIndexEntry(
                        pack.Id,
                        rawVersion,
                        $"Pack version '{rawVersion}' appears more than once for pack '{pack.Id}'."));
                    continue;
                }

                var outcome = PackVersionParser.Parse(versionElement, pack.Id, source, cancellationToken);
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
                pack.Id, validVersions, rejectedVersions, unknownVersions, indexStatus, indexStatusError));
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return ParseOutcome<ParsedPack>.NewMalformed(new RejectedIndexEntry(pack.Id, ex.Message));
        }
    }

    private static HashSet<ModVersion> FindDuplicateVersions(
        IReadOnlyList<JsonElement>? versions,
        CancellationToken cancellationToken)
    {
        if (versions is null)
            return [];

        var counts = new Dictionary<ModVersion, int>();
        foreach (var versionElement in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawVersion = IndexJsonHelpers.TryExtractNestedString(versionElement, "authored", "version");
            if (ModVersion.TryParse(rawVersion, out var version))
                counts[version] = counts.GetValueOrDefault(version) + 1;
        }

        return counts.Where(pair => pair.Value > 1).Select(pair => pair.Key).ToHashSet();
    }
}
