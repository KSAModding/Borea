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
    public static bool ValidateIndexRoot(byte[] body, HttpResponseMessage response) =>
        ValidateDownloaded(body, response, "index", ValidateIndexRoot);

    /// <summary>
    /// Validates the root of a downloaded type file the way <see cref="ValidateIndexRoot(byte[], HttpResponseMessage)"/>
    /// validates the index: the shared envelope and the one array of the type.
    /// </summary>
    /// <exception cref="HttpRequestException"></exception>
    public static bool ValidateTypeFileRoot(byte[] body, HttpResponseMessage response, ContentIndexTypeFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return ValidateDownloaded(body, response, file.FileName, document => ValidateTypeFileRoot(document, file));
    }

    /// <summary>
    /// The ValidateIndexRoot overload for cached snapshot readers that turns a
    /// <see cref="string"/> into a <see cref="JsonDocument"/> before validation.
    /// <br /><br />
    /// Validates the Root structure of the Content Index.
    /// Rejects invalid JSON, non-object roots, unsupported snapshot_version,
    /// non array listing or packs, and a non-object game_versions.
    /// </summary>
    /// <param name="indexFile">The string containing the entire index file</param>
    /// <param name="indexPath">The path or source name used in error messages.</param>
    /// <exception cref="InvalidOperationException"></exception>
    public static bool ValidateIndexRoot(string indexFile, string indexPath) =>
        ValidateIndexRoot(Parse(indexFile, indexPath, "Index file"));

    /// <summary>
    /// Validates the root of a cached type file: the shared envelope and the one array of the type.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static bool ValidateTypeFileRoot(string text, string path, ContentIndexTypeFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return ValidateTypeFileRoot(Parse(text, path, file.FileName), file);
    }

    private static bool ValidateDownloaded(byte[] body, HttpResponseMessage response, string name, Func<JsonDocument, bool> validate)
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
            throw new HttpRequestException($"Failed to parse {name} as JSON", ex);
        }

        // try-catch converts the InvalidOperationException
        // into a HttpRequestException
        bool result;
        try
        {
            result = validate(document);
        }
        catch (InvalidOperationException ex)
        {
            throw new HttpRequestException($"{ex}");
        }

        return result;
    }

    private static JsonDocument Parse(string text, string path, string name)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException($"{name} is empty at {path}");
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch
        {
            throw new InvalidOperationException($"{name} is not valid JSON at {path}");
        }
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
            ValidateEnvelope(root, "The index");

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

    private static bool ValidateTypeFileRoot(JsonDocument document, ContentIndexTypeFile file)
    {
        using (document)
        {
            JsonElement root = document.RootElement;
            ValidateEnvelope(root, file.FileName);

            if (!root.TryGetProperty(file.ArrayKey, out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"{file.FileName} has no usable '{file.ArrayKey}'.");
            }
        }
        return true;
    }

    /// <summary>The object root, snapshot_version and sources, which every snapshot file shares.</summary>
    private static void ValidateEnvelope(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{name} is not a JSON object.");
        }

        // Reject if no usable snapshot_version or value is 0 or below
        if (!root.TryGetProperty("snapshot_version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out int snapshotVersion) ||
            snapshotVersion < 1)
        {
            throw new InvalidOperationException($"{name} has no usable 'snapshot_version'.");
        }

        // A newer envelope is refused whole and the cached copy is kept.
        if (SnapshotVersions.IsAboveHighest(snapshotVersion))
        {
            throw new InvalidOperationException($"{name} is snapshot version {snapshotVersion} and this build reads {SnapshotVersions.Highest}.");
        }

        // If sources exist, make sure it is an object
        if (root.TryGetProperty("sources", out var sourcesElement))
        {
            if (sourcesElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{name} has a 'sources' that is not an object.");
            }
        }
    }
}
