using System.Globalization;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>Reads the optional published_at, updated_at and listed_at values of one listing or pack entry.</summary>
internal static class ContentDatesParser
{
    /// <summary>
    /// A bad date drops only itself. An updated_at before published_at drops both, because the entry does not say which one is wrong.
    /// listed_at comes from the history of the index, not from the releases, so no order to the other two applies to it.
    /// </summary>
    public static (DateTimeOffset? PublishedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? ListedAt, IReadOnlyList<RejectedIndexEntry> Errors) Parse(
        JsonElement entry,
        string id)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return (null, null, null, Array.Empty<RejectedIndexEntry>());

        var errors = new List<RejectedIndexEntry>();
        var publishedAt = Read(entry, "published_at", id, errors);
        var updatedAt = Read(entry, "updated_at", id, errors);
        var listedAt = Read(entry, "listed_at", id, errors);

        if (publishedAt is { } published && updatedAt is { } updated && updated < published)
        {
            errors.Add(new RejectedIndexEntry(
                id,
                $"The updated_at value '{updated:O}' is before the published_at value '{published:O}', so neither date is used."));
            return (null, null, listedAt, errors);
        }

        return (publishedAt, updatedAt, listedAt, errors);
    }

    private static DateTimeOffset? Read(JsonElement entry, string propertyName, string id, List<RejectedIndexEntry> errors)
    {
        if (!entry.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind != JsonValueKind.String)
        {
            errors.Add(new RejectedIndexEntry(id, $"The {propertyName} value is unreadable. It must be a string, but was {property.ValueKind}."));
            return null;
        }

        var text = property.GetString()!;
        if (!text.Contains('T', StringComparison.Ordinal)
            || !text.EndsWith('Z')
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            errors.Add(new RejectedIndexEntry(id, $"The {propertyName} value is unreadable. '{text}' is not an ISO 8601 UTC timestamp."));
            return null;
        }

        return parsed;
    }
}
