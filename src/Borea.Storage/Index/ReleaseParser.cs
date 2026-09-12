using Borea.Core.Mods;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Parses one entry of a listing's releases array. A release that
/// fails only rejects itself; the listing it belongs to stays valid.
/// </summary>
public static class ReleaseParser
{
    public static ParseOutcome<ReleasesEntryDto> Parse(JsonElement element)
    {
        var id = IndexJsonHelpers.TryExtractString(element, "id");
        var version = IndexJsonHelpers.TryExtractString(element, "version");
        var label = IndexJsonHelpers.DescribeEntry(id, version);

        var specVersion = IndexJsonHelpers.TryExtractInt(element, "spec_version");
        if (specVersion is { } sv)
        {
            if (SpecVersions.IsAboveHighest(sv))
                return ParseOutcome<ReleasesEntryDto>.NewUnknown(new UnknownIndexVersionEntry(label, sv));

            if (sv < 1)
                return ParseOutcome<ReleasesEntryDto>.NewMalformed(new RejectedIndexEntry(label, $"The release declares spec_version {sv}, which is not valid."));
        }

        try
        {
            var release = element.Deserialize<ReleasesEntryDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The release deserialized to null.");

            return ParseOutcome<ReleasesEntryDto>.Valid(release);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return ParseOutcome<ReleasesEntryDto>.NewMalformed(new RejectedIndexEntry(label, ex.Message));
        }
    }
}
