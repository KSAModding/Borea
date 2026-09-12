using Borea.Core.Mods;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Parses one entry of a pack's versions array. A version that fails only rejects
/// itself; the pack it belongs to stays valid.
/// </summary>
public static class PackVersionParser
{
    public static ParseOutcome<PackVersionDto> Parse(JsonElement element)
    {
        var id = IndexJsonHelpers.TryExtractNestedString(element, "authored", "id");
        var version = IndexJsonHelpers.TryExtractNestedString(element, "authored", "version");
        var label = IndexJsonHelpers.DescribeEntry(id, version);

        var specVersion = IndexJsonHelpers.TryExtractNestedInt(element, "authored", "spec_version");
        if (specVersion is { } sv)
        {
            if (SpecVersions.IsAboveHighest(sv))
                return ParseOutcome<PackVersionDto>.NewUnknown(new UnknownIndexVersionEntry(label, sv));

            if (sv < 1)
                return ParseOutcome<PackVersionDto>.NewMalformed(new RejectedIndexEntry(label, $"The pack version declares spec_version {sv}, which is not valid."));
        }

        try
        {
            var packVersion = element.Deserialize<PackVersionDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The pack version deserialized to null.");

            return ParseOutcome<PackVersionDto>.Valid(packVersion);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return ParseOutcome<PackVersionDto>.NewMalformed(new RejectedIndexEntry(label, ex.Message));
        }
    }
}
