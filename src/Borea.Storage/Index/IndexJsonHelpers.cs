using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Best-effort field extraction from a raw <see cref="JsonElement"/>. Used to
/// recover an id, a version, or a spec_version before a full DTO deserialize
/// is attempted.
/// </summary>
internal static class IndexJsonHelpers
{
    public static string? TryExtractString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    public static int? TryExtractInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }

    public static string? TryExtractNestedString(JsonElement element, string parentProperty, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(parentProperty, out var parent) &&
            parent.ValueKind == JsonValueKind.Object)
        {
            return TryExtractString(parent, propertyName);
        }

        return null;
    }

    public static int? TryExtractNestedInt(JsonElement element, string parentProperty, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(parentProperty, out var parent) &&
            parent.ValueKind == JsonValueKind.Object)
        {
            return TryExtractInt(parent, propertyName);
        }

        return null;
    }

    /// <summary>A short label for a rejected/unknown entry when only some of its identity is known.</summary>
    public static string? DescribeEntry(string? id, string? version) => (id, version) switch
    {
        (null, null) => null,
        (var i, null) => i,
        (null, var v) => $"version {v}",
        (var i, var v) => $"{i} {v}",
    };
}
