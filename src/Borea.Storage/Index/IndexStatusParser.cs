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

            var dto = new IndexStatusDto
            {
                State = ReadRequiredString(element, "state"),
                Reason = ReadOptionalString(element, "reason"),
            };
            var statusWithoutTimestamp = DtoMapper.MapIndexStatus(dto);

            if (!element.TryGetProperty("since", out var sinceElement))
                return (statusWithoutTimestamp, null);

            if (sinceElement.ValueKind != JsonValueKind.String)
            {
                return (
                    statusWithoutTimestamp,
                    TimestampError(id, version, $"The since value must be a string, but was {sinceElement.ValueKind}."));
            }

            dto.Since = sinceElement.GetString();

            try
            {
                return (DtoMapper.MapIndexStatus(dto), null);
            }
            catch (FormatException ex) when (dto.Since is not null)
            {
                return (statusWithoutTimestamp, TimestampError(id, version, ex.Message));
            }
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return (null, new RejectedIndexEntry(id, version, $"The index_status value is unreadable. {ex.Message}"));
        }
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            throw new FormatException($"The index_status value must contain {propertyName}.");

        return ReadString(property, propertyName);
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        return ReadString(property, propertyName);
    }

    private static string ReadString(JsonElement property, string propertyName)
    {
        if (property.ValueKind != JsonValueKind.String)
            throw new FormatException($"The index_status {propertyName} value must be a string, but was {property.ValueKind}.");

        return property.GetString()!;
    }

    private static RejectedIndexEntry TimestampError(string id, string? version, string reason) =>
        new(id, version, $"The index_status timestamp is unreadable. {reason}");
}
