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
            return TryReadText(property);
        }

        return null;
    }

    /// <summary>
    /// The text of a string value. An escaped lone surrogate such as \ud800 is valid JSON but not valid Unicode text,
    /// and <see cref="JsonElement.GetString"/> throws <see cref="InvalidOperationException"/> for it, so this throws
    /// the <see cref="FormatException"/> that every index reader treats as bad input.
    /// </summary>
    public static string ReadText(JsonElement value)
    {
        try
        {
            return value.GetString()!;
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException("A string value is not valid Unicode text.", ex);
        }
    }

    /// <summary>The text of a string value, or null when it is not valid Unicode text.</summary>
    public static string? TryReadText(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

    /// <summary>True for failures caused by index input instead of a Borea programming defect.</summary>
    public static bool IsInputFailure(Exception exception) =>
        exception is JsonException or FormatException or IndexInputException;
}

internal sealed class IndexInputException : ArgumentException
{
    public IndexInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
