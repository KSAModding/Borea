using Borea.Core.Index;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>Reads the optional downloads value of one listing by hand, so a duplicate host key or a non-integer count is found.</summary>
internal static class DownloadCountsParser
{
    /// <summary>A bad total or host value drops all counts of the listing. A bad release entry drops only itself.</summary>
    public static (ListingDownloadCounts? Counts, IReadOnlyList<RejectedIndexEntry> Errors) Parse(JsonElement container, string id)
    {
        if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty("downloads", out var element))
            return (null, Array.Empty<RejectedIndexEntry>());

        try
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new FormatException($"The downloads value must be an object, but was {element.ValueKind}.");

            var dto = new DownloadCountsDto
            {
                Total = ReadCount(element, "total", "downloads"),
                Hosts = ReadHosts(element, "downloads"),
            };
            var errors = new List<RejectedIndexEntry>();
            var releases = ReadReleases(element, id, errors);
            return (DtoMapper.MapDownloadCounts(dto, releases), errors);
        }
        catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
        {
            return (null, new[] { new RejectedIndexEntry(id, $"The downloads value is unreadable. {ex.Message}") });
        }
    }

    private static IReadOnlyList<ReleaseDownloadCounts> ReadReleases(
        JsonElement downloads,
        string id,
        List<RejectedIndexEntry> errors)
    {
        if (!downloads.TryGetProperty("releases", out var releases))
            return [];

        if (releases.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new RejectedIndexEntry(
                id,
                $"The downloads releases value must be an array, but was {releases.ValueKind}, so no version has a known count."));
            return [];
        }

        var candidates = new List<ReleaseDownloadCounts>(releases.GetArrayLength());
        var index = 0;
        foreach (var release in releases.EnumerateArray())
        {
            var location = $"downloads releases item at index {index}";
            try
            {
                candidates.Add(DtoMapper.MapReleaseDownloadCounts(ReadRelease(release, location)));
            }
            catch (Exception ex) when (IndexJsonHelpers.IsInputFailure(ex))
            {
                errors.Add(new RejectedIndexEntry(
                    id,
                    IndexJsonHelpers.TryExtractString(release, "version"),
                    $"The {location} is unreadable. {ex.Message}"));
            }

            index++;
        }

        var duplicates = candidates
            .GroupBy(release => release.Version)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var duplicate in candidates.Where(release => duplicates.Contains(release.Version)))
        {
            errors.Add(new RejectedIndexEntry(
                id,
                duplicate.Version.ToString(),
                $"Version '{duplicate.Version}' has more than one download count."));
        }

        return candidates.Where(release => !duplicates.Contains(release.Version)).ToArray();
    }

    private static ReleaseDownloadCountsDto ReadRelease(JsonElement release, string location)
    {
        if (release.ValueKind != JsonValueKind.Object)
            throw new FormatException($"The {location} must be an object, but was {release.ValueKind}.");

        if (!release.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String)
            throw new FormatException($"The {location} must contain a string version.");

        return new ReleaseDownloadCountsDto
        {
            Version = version.GetString()!,
            Total = ReadCount(release, "total", location),
            Hosts = ReadHosts(release, location),
        };
    }

    private static Dictionary<string, long> ReadHosts(JsonElement container, string location)
    {
        if (!container.TryGetProperty("hosts", out var hosts))
            throw new FormatException($"The {location} must contain hosts.");

        if (hosts.ValueKind != JsonValueKind.Object)
            throw new FormatException($"The {location} hosts value must be an object, but was {hosts.ValueKind}.");

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var host in hosts.EnumerateObject())
        {
            if (result.ContainsKey(host.Name))
                throw new FormatException($"The {location} names host '{host.Name}' more than once.");

            result.Add(host.Name, ReadCountValue(host.Value, $"{location} host '{host.Name}'"));
        }

        return result;
    }

    private static long ReadCount(JsonElement container, string propertyName, string location)
    {
        if (!container.TryGetProperty(propertyName, out var property))
            throw new FormatException($"The {location} must contain {propertyName}.");

        return ReadCountValue(property, $"{location} {propertyName}");
    }

    private static long ReadCountValue(JsonElement value, string location)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var count))
            throw new FormatException($"The {location} value must be a whole number.");

        if (count < 0)
            throw new FormatException($"The {location} value cannot be negative.");

        return count;
    }
}
