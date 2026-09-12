using Borea.Core.Index;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

internal static class IndexStatusParser
{
    public static (IndexStatus? Status, RejectedIndexEntry? Error) Parse(
        JsonElement container,
        string id,
        string? version = null)
    {
        if (!container.TryGetProperty("index_status", out var element))
            return (null, null);

        try
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new FormatException("The index_status value must be an object.");

            var dto = element.Deserialize<IndexStatusDto>(IndexJsonOptions.Value)
                ?? throw new JsonException("The index_status value deserialized to null.");

            return (DtoMapper.MapIndexStatus(dto), null);
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return (null, new RejectedIndexEntry(id, version, $"The index_status value is unreadable. {ex.Message}"));
        }
    }
}
