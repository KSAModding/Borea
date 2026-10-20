using Borea.Core.Mods;
using Borea.Storage.Index.Dtos;
using MetadataEnumMapper = Borea.Storage.Mods.MetadataEnumMapper;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Parses one entry of a listing's releases array. A release that
/// fails only rejects itself; the listing it belongs to stays valid.
/// </summary>
public static class ReleaseParser
{
    public static ParseOutcome<ModVersionMetadata> Parse(
        JsonElement element,
        string listingId,
        string source,
        ModMetadata? authored,
        string? changelogText = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = IndexJsonHelpers.TryExtractString(element, "id");
        var version = IndexJsonHelpers.TryExtractString(element, "version");

        var specVersion = IndexJsonHelpers.TryExtractInt(element, "spec_version");
        if (specVersion is { } sv)
        {
            if (SpecVersions.IsAboveHighest(sv))
                return ParseOutcome<ModVersionMetadata>.NewUnknown(new UnknownIndexVersionEntry(
                    id,
                    version,
                    sv,
                    $"Release '{id}' version '{version}' uses unsupported spec_version {sv}."));

            if (sv < 1)
                return ParseOutcome<ModVersionMetadata>.NewMalformed(new RejectedIndexEntry(id, version, $"The release declares spec_version {sv}, which is not valid."));
        }

        try
        {
            var release = element.Deserialize<ReleasesEntryDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The release deserialized to null.");
            cancellationToken.ThrowIfCancellationRequested();

            if (!ModIds.Equals(listingId, release.Id))
            {
                return ParseOutcome<ModVersionMetadata>.NewMalformed(new RejectedIndexEntry(
                    id,
                    version,
                    $"Release id '{release.Id}' does not agree with listing id '{listingId}'."));
            }

            // Only vehicles and saves are compared, so every mod and loader release keeps the verdict it had before they existed.
            var type = MetadataEnumMapper.ParseContentType(release.Type);
            if ((type.IsVehicleOrSave() || authored?.Type.IsVehicleOrSave() == true) && type != authored?.Type)
            {
                return ParseOutcome<ModVersionMetadata>.NewMalformed(new RejectedIndexEntry(
                    id,
                    version,
                    $"Release type '{release.Type}' does not agree with the type of listing '{listingId}'."));
            }

            return ParseOutcome<ModVersionMetadata>.Valid(DtoMapper.MapRelease(release, source, authored, changelogText));
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return ParseOutcome<ModVersionMetadata>.NewMalformed(new RejectedIndexEntry(id, version, ex.Message));
        }
    }
}
