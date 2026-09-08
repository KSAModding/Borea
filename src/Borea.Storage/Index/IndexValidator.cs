using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Storage.Paths;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace Borea.Storage.Index;

/// <summary>
/// This class is used to validate the data in the index.json file.
/// </summary>
public class IndexValidator
{
    private GamePathProvider _pathProvider;

    public IndexValidator(GamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    /// <summary>
    /// Validates the index.json file and returns true if it is valid. Throws an exception with the reason if it is not valid.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>"
    public bool ValidateIndex()
    {
        string indexPath = _pathProvider.GetIndexPath();

        string indexFile = ReadIndex(indexPath);

        if (string.IsNullOrEmpty(indexFile))
        {
            throw new InvalidOperationException($"Index file is empty at {indexPath}");
        }

        JsonDocument indexJson;
        try
        {
            indexJson = JsonDocument.Parse(indexFile);
        }
        catch
        {
            throw new InvalidOperationException($"Index file is not valid JSON at {indexPath}");
        }

        var rootElement = indexJson.RootElement;

        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Index file is not a valid JSON object at {indexPath}");
        }

        // This checks the index.json file to see if snapshot_version is valid.
        if (!rootElement.TryGetProperty("snapshot_version", out var snapshotVersionElement)
            || !snapshotVersionElement.TryGetInt32(out int snapshotVersion)
            || snapshotVersion <= 0
            || snapshotVersion > SnapshotVersions.Highest)
        {
            throw new InvalidOperationException($"Index file has an invalid snapshot version at {indexPath}");
        }



        return true;
    }

    /// <summary>
    /// Validates the listings in the index.json file and returns true if valid.
    /// Throws an exception with the reason if it is not valid.
    /// </summary>
    /// <returns></returns>
    private static void ValidateListing(JsonElement listing)
    {
        // Required
        if (!listing.TryGetProperty("id", out var idElement) || !ModIds.IsValid(idElement.GetString()))
        {
            return;
        }

        // If index_status exists, make sure it is valid
        if (listing.TryGetProperty("index_status", out var indexStatusElement))
        {
            if (!ValidateIndexStatus(indexStatusElement, out IndexStatus? indexStatus))
            {
                return;
            }
        }

        // Optional, but if it exists, validate the object
        if (listing.TryGetProperty("authored", out var authoredElement))
        {
            if (!ValidateAuthoredSection(authoredElement, out ModMetadata metadata))
            {
                return;
            }
        }

        // Optional, but if it exists, validate the objects
        if (listing.TryGetProperty("releases", out var releasesElement))
        {
            foreach (var release in releasesElement.EnumerateArray())
            {
                // ValidateRelease(release);
            }
        }
    }

    public static bool ValidateAuthoredSection(JsonElement authoredElement, out ModMetadata? metadata)
    {
        metadata = null;

        // If not correct object, reject
        if (authoredElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Required
        if (!authoredElement.TryGetProperty("spec_version", out var specVersionElement)
            || !specVersionElement.TryGetInt32(out int specVersion)
            || specVersion <= 0
            || specVersion > SpecVersions.Highest)
        {
            return false;
        }

        // Required
        if (!authoredElement.TryGetProperty("id", out var idElement) || !ModIds.IsValid(idElement.GetString()))
        {
            return false;
        }

        if (!authoredElement.TryGetProperty("type", out var typeElement))
        {
            return false;
        }

       
        if (!Enum.TryParse<ContentType>(typeElement.GetString(), true, out var type))
        {
            type = ContentType.Unknown;
        }

        authoredElement.TryGetProperty("name", out var nameLement);
        authoredElement.TryGetProperty("authors", out var authorsElement);
        authoredElement.TryGetProperty("abstract", out var abstractElement);
        authoredElement.TryGetProperty("description", out var descriptionElement);
        authoredElement.TryGetProperty("license", out var licenseElement);
        authoredElement.TryGetProperty("tags", out var tagsElement);
        authoredElement.TryGetProperty("status", out var statusElement);
        authoredElement.TryGetProperty("superseded_by", out var supersededByElement);
        authoredElement.TryGetProperty("links", out var linksElement);
        authoredElement.TryGetProperty("", out var);

        return true;
    }

    public static bool ValidateIndexStatus(JsonElement indexStatusElement, out IndexStatus? indexStatus)
    {
        // If it can't parse, it is null
        indexStatus = null;
        if (indexStatusElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Required if "index_status" exists, must be a string
        if (!indexStatusElement.TryGetProperty("state", out var stateElement)
            || stateElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        // If it can't parse, it is unknown
        if (!Enum.TryParse(stateElement.GetString(), true, out IndexStatusState state)
            || int.TryParse(stateElement.GetString(), out _))
        {
            state = IndexStatusState.Unknown;
        }

        // Trys to get out a valid element and value, but optional
        indexStatusElement.TryGetProperty("since", out var sinceElement);
        indexStatusElement.TryGetProperty("reason", out var reasonElement);

        string? since = null;
        string? reason = null;

        // Swallows type errors but that is fine since only state is important
        try { since = sinceElement.GetString(); } catch { }
        try { reason = reasonElement.GetString(); } catch { }

        indexStatus = new IndexStatus(state, since, reason);

        return true;
    }

    public string ReadIndex(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"Index does not exist at {path}");
        }

        var indexFile = File.ReadAllText(path);

        return indexFile;
    }

    public void WriteIndex(string path, string content)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException($"Index path is null or empty");
        }
        File.WriteAllText(path, content);
    }
}
