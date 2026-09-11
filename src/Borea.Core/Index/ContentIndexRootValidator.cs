using System.Text.Json;

namespace Borea.Core.Index;

public static class ContentIndexRootValidator
{
    /// <summary>
    /// The ValidateIndexRoot overload for <see cref="IContentIndexFetcher"/>
    /// that turns a <see cref="byte"/>[] into a <see cref="JsonDocument"/> before validation.
    /// <br /><br />
    /// Validates the Root structure of the Content Index.
    /// Rejects invalid JSON, non-object roots, unsupported snapshot_version,
    /// non array listing or packs, and a non-object game_versions.
    /// </summary>
    /// <exception cref="HttpRequestException"></exception>
    public static bool ValidateIndexRoot(byte[] body, HttpResponseMessage response)
    {
        // Make sure index is JSON or plain text
        if (response.Content.Headers.ContentType?.MediaType != "application/json" && response.Content.Headers.ContentType?.MediaType != "text/plain")
        {
            throw new HttpRequestException($"Expected content type 'application/json' or 'text/plain' but got '{response.Content.Headers.ContentType?.MediaType}'");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Failed to parse index as JSON", ex);
        }

        // try-catch converts the InvalidOperationException
        // into a HttpRequestException
        bool result;
        try
        {
            result = ValidateIndexRoot(document);
        }
        catch (InvalidOperationException ex)
        {
            throw new HttpRequestException($"{ex}");
        }
        return result;
    }

    /// <summary>
    /// The ValidateIndexRoot overload for IndexValidator that turns a
    /// <see cref="string"/> into a <see cref="JsonDocument"/> before validation.
    /// <br /><br />
    /// Validates the Root structure of the Content Index.
    /// Rejects invalid JSON, non-object roots, unsupported snapshot_version,
    /// non array listing or packs, and a non-object game_versions.
    /// </summary>
    /// <param name="indexFile">The string containing the entire index file</param>
    /// <param name="indexPath">sthe path to the index file</param>
    /// <exception cref="InvalidOperationException"></exception>
    public static bool ValidateIndexRoot(string indexFile, string indexPath)
    {
        JsonDocument indexJson;
        try
        {
            indexJson = JsonDocument.Parse(indexFile);
        }
        catch
        {
            throw new InvalidOperationException($"Index file is not valid JSON at {indexPath}");
        }

        return ValidateIndexRoot(indexJson);
    }

    /// <summary>
    /// Validates the Root structure of the Content Index.
    /// Rejects non-object roots, unsupported snapshot_version,
    /// non array listing or packs, and a non-object game_versions.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static bool ValidateIndexRoot(JsonDocument document)
    {
        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("The index is not a JSON object.");
            }

            // Reject if no usable snapshot_version or value is 0 or below
            if (!root.TryGetProperty("snapshot_version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out int snapshotVersion) ||
                snapshotVersion < 1)
            {
                throw new InvalidOperationException("The index has no usable 'snapshot_version'.");
            }

            // A newer envelope is refused whole and the cached copy is kept.
            if (SnapshotVersions.IsAboveHighest(snapshotVersion))
            {
                throw new InvalidOperationException($"The index is snapshot version {snapshotVersion} and this build reads {SnapshotVersions.Highest}.");
            }

            // If sources exist, make sure it is an object
            if (root.TryGetProperty("sources", out var sourcesElement))
            {
                if (sourcesElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("'sources' is not an object.");
                }
            }

            if (!root.TryGetProperty("listings", out var listingsElement)
                || listingsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The index has no usable 'listings'.");
            }

            if (!root.TryGetProperty("packs", out var packsElement)
                || packsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The index has no usable 'packs'.");
            }

            if (!root.TryGetProperty("game_versions", out var gameVersionsElements)
                || gameVersionsElements.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("The index has no usable 'game_versions'.");
            }
        }
        return true;
    }
}
