using Borea.Core.Index;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>Reads the optional images value of one authored document by hand, so one bad record drops only itself.</summary>
internal static class ContentImagesParser
{
    /// <summary>A bad images or description value drops what it holds. A bad icon or description record drops only itself.</summary>
    public static (ContentImages? Images, IReadOnlyList<RejectedIndexEntry> Errors) Parse(
        JsonElement document,
        string id,
        string? version = null)
    {
        if (document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("images", out var element))
            return (null, Array.Empty<RejectedIndexEntry>());

        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, new[]
            {
                new RejectedIndexEntry(id, version, $"The images value is unreadable. The images value must be an object, but was {element.ValueKind}."),
            });
        }

        var errors = new List<RejectedIndexEntry>();
        var icon = ReadIcon(element, id, version, errors);
        var description = ReadDescription(element, id, version, errors);
        var images = icon is null && description.Count == 0 ? null : new ContentImages(icon, description);
        return (images, errors);
    }

    private static IconImage? ReadIcon(JsonElement images, string id, string? version, List<RejectedIndexEntry> errors)
    {
        if (!images.TryGetProperty("icon", out var icon))
            return null;

        try
        {
            return DtoMapper.MapIconImage(ReadImage(icon, "images icon"));
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            errors.Add(new RejectedIndexEntry(id, version, $"The images icon is unreadable. {ex.Message}"));
            return null;
        }
    }

    private static IReadOnlyList<DescriptionImage> ReadDescription(
        JsonElement images,
        string id,
        string? version,
        List<RejectedIndexEntry> errors)
    {
        if (!images.TryGetProperty("description", out var description))
            return [];

        if (description.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new RejectedIndexEntry(
                id,
                version,
                $"The images description value must be an array, but was {description.ValueKind}, so no description image is used."));
            return [];
        }

        if (description.GetArrayLength() > ContentImages.MaxDescriptionImages)
        {
            errors.Add(new RejectedIndexEntry(
                id,
                version,
                $"The images description value has {description.GetArrayLength()} images, more than {ContentImages.MaxDescriptionImages}, so no description image is used."));
            return [];
        }

        var duplicates = description.EnumerateArray()
            .Select(ReadIdOrNull)
            .OfType<string>()
            .GroupBy(imageId => imageId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = new List<DescriptionImage>(description.GetArrayLength());
        var index = 0;
        foreach (var record in description.EnumerateArray())
        {
            var location = $"images description item at index {index}";
            try
            {
                candidates.Add(DtoMapper.MapDescriptionImage(ReadId(record, location), ReadImage(record, location)));
            }
            catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
            {
                errors.Add(new RejectedIndexEntry(id, version, $"The {location} is unreadable. {ex.Message}"));
            }

            index++;
        }

        foreach (var duplicate in duplicates)
            errors.Add(new RejectedIndexEntry(id, version, $"Description image id '{duplicate}' appears more than once, so no image with that id is used."));

        return candidates.Where(image => !duplicates.Contains(image.Id)).ToArray();
    }

    private static string ReadId(JsonElement record, string location)
    {
        if (record.ValueKind != JsonValueKind.Object)
            throw new FormatException($"The {location} must be an object, but was {record.ValueKind}.");

        return ReadRequiredString(record, "id", location);
    }

    private static string? ReadIdOrNull(JsonElement record) =>
        record.ValueKind == JsonValueKind.Object
        && record.TryGetProperty("id", out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static ImageDto ReadImage(JsonElement record, string location)
    {
        if (record.ValueKind != JsonValueKind.Object)
            throw new FormatException($"The {location} must be an object, but was {record.ValueKind}.");

        return new ImageDto
        {
            Url = ReadRequiredString(record, "url", location),
            Sha256 = ReadRequiredString(record, "sha256", location),
            Width = ReadPixels(record, "width", location),
            Height = ReadPixels(record, "height", location),
            Size = ReadSize(record, location),
            License = ReadOptionalString(record, "license", location),
            Attribution = ReadOptionalString(record, "attribution", location),
            Source = ReadOptionalString(record, "source", location),
        };
    }

    private static string ReadRequiredString(JsonElement record, string propertyName, string location)
    {
        if (!record.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new FormatException($"The {location} must contain a string {propertyName}.");

        return property.GetString()!;
    }

    private static string? ReadOptionalString(JsonElement record, string propertyName, string location)
    {
        if (!record.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        if (property.ValueKind != JsonValueKind.String)
            throw new FormatException($"The {location} {propertyName} value must be a string, but was {property.ValueKind}.");

        return property.GetString();
    }

    private static int ReadPixels(JsonElement record, string propertyName, string location)
    {
        if (!record.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var pixels))
            throw new FormatException($"The {location} {propertyName} value must be a whole number.");

        return pixels;
    }

    private static long ReadSize(JsonElement record, string location)
    {
        if (!record.TryGetProperty("size", out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out var size))
            throw new FormatException($"The {location} size value must be a whole number.");

        return size;
    }
}
