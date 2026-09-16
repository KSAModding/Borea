using Borea.Core.Mods;
using System.Text;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>Reads the optional changelog_text value of one release by hand, so a bad value drops only itself and not the release.</summary>
internal static class ChangelogTextParser
{
    public static (string? Text, RejectedIndexEntry? Error) Parse(JsonElement release, string id, string? version)
    {
        if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("changelog_text", out var property))
            return (null, null);

        if (property.ValueKind != JsonValueKind.String)
            return Drop(id, version, $"The changelog_text value must be a string, but was {property.ValueKind}, so it is not used.");

        string text;
        try
        {
            text = property.GetString()!;
        }
        catch (InvalidOperationException)
        {
            return Drop(id, version, "The changelog_text value is not valid Unicode text, so it is not used.");
        }

        var bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes > ModVersionMetadata.MaxChangelogTextBytes)
            return Drop(id, version, $"The changelog_text value has {bytes} bytes of UTF-8, more than {ModVersionMetadata.MaxChangelogTextBytes}, so it is not used.");

        return (text, null);
    }

    private static (string? Text, RejectedIndexEntry? Error) Drop(string id, string? version, string reason) =>
        (null, new RejectedIndexEntry(id, version, reason));
}
