using Borea.Core.Mods;
using Borea.Core.ModPacks;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Parses one entry of a pack's versions array. A version that fails only rejects
/// itself; the pack it belongs to stays valid.
/// </summary>
public static class PackVersionParser
{
    public static ParseOutcome<ParsedPackVersion> Parse(
        JsonElement element,
        string packId,
        string source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = IndexJsonHelpers.TryExtractNestedString(element, "authored", "id");
        var version = IndexJsonHelpers.TryExtractNestedString(element, "authored", "version");

        var specVersion = IndexJsonHelpers.TryExtractNestedInt(element, "authored", "spec_version");
        if (specVersion is { } sv)
        {
            if (SpecVersions.IsAboveHighest(sv))
                return ParseOutcome<ParsedPackVersion>.NewUnknown(new UnknownIndexVersionEntry(
                    id,
                    version,
                    sv,
                    $"Pack '{id}' version '{version}' uses unsupported spec_version {sv}."));

            if (sv < 1)
                return ParseOutcome<ParsedPackVersion>.NewMalformed(new RejectedIndexEntry(id, version, $"The pack version declares spec_version {sv}, which is not valid."));
        }

        try
        {
            var packVersion = element.Deserialize<PackVersionDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The pack version deserialized to null.");
            cancellationToken.ThrowIfCancellationRequested();

            if (!ModIds.Equals(packId, packVersion.Authored.Id))
            {
                return ParseOutcome<ParsedPackVersion>.NewMalformed(new RejectedIndexEntry(
                    id,
                    version,
                    $"Pack version id '{packVersion.Authored.Id}' does not agree with pack id '{packId}'."));
            }

            var metadata = DtoMapper.MapPackVersion(packVersion.Authored, source);
            var (indexStatus, indexStatusError) = IndexStatusParser.Parse(element, packId, version);
            return ParseOutcome<ParsedPackVersion>.Valid(new ParsedPackVersion(metadata, indexStatus, indexStatusError));
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return ParseOutcome<ParsedPackVersion>.NewMalformed(new RejectedIndexEntry(id, version, ex.Message));
        }
    }
}
