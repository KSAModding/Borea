using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Network.SpaceDock;

/// <summary>
/// IModRepository against SpaceDock's public API, mapping its data onto the
/// RFC 0031 metadata types. SpaceDock cannot provide most authored facts, so
/// the mapping fills in:
///
/// - The id is a placeholder, SpaceDock's numeric mod id stringified; the
///   true id is only knowable after a download (SpaceDockResolver).
/// - An empty dependency list and a null loader mean unknown, not none.
/// - The forums link is the website when it points at the KSA forums,
///   otherwise the SpaceDock page stands in (the model requires one).
/// - game_min is the oldest game version any release claims (RFC 0017).
/// - Checksum and sizes are null; the API does not expose them.
///
/// A KSA listing whose data does not parse still surfaces with placeholder
/// fields; only its unusable releases are skipped by the release accessors.
/// </summary>
public sealed class SpaceDockModRepository : IModRepository
{
    // SpaceDock's internal database ID for KSA.
    private const int KsaGameId = 22409;

    private const string SourceName = "spacedock";
    private const string BaseUrl = "https://spacedock.info";
    private const string KsaForumsHost = "forums.ahwoo.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;
    private readonly SpaceDockResolver _resolver;

    public SpaceDockModRepository(HttpClient httpClient, SpaceDockResolver resolver)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
    {
        // Single page: /api/browse pages at 500 and IModRepository has no
        // paging concept. Revisit if KSA's catalog outgrows one page.
        var response = await _httpClient.GetFromJsonAsync<SpaceDockBrowseResponseDto>(
            $"api/browse?game_id={KsaGameId}&count=500", JsonOptions, cancellationToken).ConfigureAwait(false);

        return (response?.Result ?? new()).Where(IsKsaMod).Select(MapToListing).ToList();
    }

    public async Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default)
    {
        var dto = await GetModAsync(modId, cancellationToken).ConfigureAwait(false);
        if (dto is null)
            return null;

        // Newest mappable release; the author-selected default version only
        // breaks ties between rows normalizing to the same version.
        return MappableReleases(dto)
            .OrderByDescending(pair => pair.Parsed)
            .ThenByDescending(pair => pair.Row.Id == dto.DefaultVersionId)
            .Select(pair => pair.Release)
            .FirstOrDefault();
    }

    public async Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
    {
        var dto = await GetModAsync(modId, cancellationToken).ConfigureAwait(false);
        if (dto is null)
            return null;

        // Same mappable set as the version list, so every listed version
        // yields a release; ties resolve like the latest accessor.
        return MappableReleases(dto)
            .Where(pair => pair.Parsed.Equals(version))
            .OrderByDescending(pair => pair.Row.Id == dto.DefaultVersionId)
            .Select(pair => pair.Release)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
    {
        var dto = await GetModAsync(modId, cancellationToken).ConfigureAwait(false);
        if (dto is null)
            return Array.Empty<ModVersion>();

        // Only versions with a usable release, newest first; Distinct because
        // different strings can normalize to one version.
        return MappableReleases(dto)
            .Select(pair => pair.Parsed)
            .Distinct()
            .OrderByDescending(v => v)
            .ToList();
    }

    public async Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        // Live search results carry game_id per mod (undocumented in api.md,
        // confirmed by real response); IsKsaMod filters on it.
        var results = await _httpClient.GetFromJsonAsync<List<SpaceDockModDto>>(
            $"api/search/mod?query={Uri.EscapeDataString(query)}", JsonOptions, cancellationToken).ConfigureAwait(false);

        return (results ?? new()).Where(IsKsaMod).Select(MapToListing).ToList();
    }

    private async Task<SpaceDockModDto?> GetModAsync(string modId, CancellationToken cancellationToken)
    {
        if (!_resolver.TryResolveId(modId, out var spaceDockId))
            return null;

        try
        {
            var dto = await _httpClient.GetFromJsonAsync<SpaceDockModDto>(
                $"api/mod/{spaceDockId}", JsonOptions, cancellationToken).ConfigureAwait(false);

            return dto is null || !IsKsaMod(dto) ? null : dto;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // A deleted or unknown id answers 404: not available, not an error.
            return null;
        }
    }

    /// <summary>
    /// True if this listing is for KSA. A missing game_id passes, because
    /// dropping a listing over an absent field is what this repository must
    /// not do.
    /// </summary>
    private static bool IsKsaMod(SpaceDockModDto dto) => dto.GameId is null or KsaGameId;

    private static ModMetadata MapToListing(SpaceDockModDto dto)
    {
        var modId = dto.Id.ToString(CultureInfo.InvariantCulture);
        var pageUrl = !string.IsNullOrWhiteSpace(dto.Url) ? $"{BaseUrl}{dto.Url}" : $"{BaseUrl}/mod/{dto.Id}";
        var website = string.IsNullOrWhiteSpace(dto.Website) ? null : dto.Website;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Authors commonly put their forums thread into the website field.
            ["forums"] = website is not null && IsKsaForumsUrl(website) ? website : pageUrl,
            ["spacedock"] = pageUrl,
        };

        if (website is not null)
            links.TryAdd("homepage", website);

        if (!string.IsNullOrWhiteSpace(dto.SourceCode))
            links.TryAdd("repository", dto.SourceCode);

        // Oldest game version any release claims, since game_min means
        // "oldest known to work" and only a too-high bound blocks an install.
        // With no parseable game version the raw string stays, so
        // compatibility evaluates to unknown.
        string? oldestRaw = null;
        var oldestRevision = int.MaxValue;
        foreach (var candidate in dto.Versions)
        {
            if (GameVersion.TryParse(candidate.RawGameVersion, out var parsed) && parsed.Revision < oldestRevision)
            {
                oldestRevision = parsed.Revision;
                oldestRaw = candidate.RawGameVersion;
            }
        }

        var defaultVersion = dto.Versions.FirstOrDefault(v => v.Id == dto.DefaultVersionId) ?? dto.Versions.FirstOrDefault();
        var gameMin = oldestRaw
            ?? (defaultVersion is not null && !string.IsNullOrWhiteSpace(defaultVersion.RawGameVersion)
                ? defaultVersion.RawGameVersion
                : "unknown");

        return new ModMetadata(
            specVersion: SpecVersions.Highest,
            modId: modId,
            source: SourceName,
            name: string.IsNullOrWhiteSpace(dto.Name) ? $"SpaceDock mod {dto.Id}" : dto.Name,
            authors: new[] { string.IsNullOrWhiteSpace(dto.Author) ? "Unknown" : dto.Author },
            abstractText: dto.ShortDescription ?? string.Empty,
            license: string.IsNullOrWhiteSpace(dto.License) ? "Unknown" : dto.License,
            links: links,
            gameMin: gameMin,
            description: string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description,
            releases: new ReleaseSource(new[] { new ReleaseHost("spacedock", modId) }));
    }

    private static IEnumerable<(ModVersion Parsed, ModVersionMetadata Release, SpaceDockVersionDto Row)> MappableReleases(SpaceDockModDto dto)
    {
        foreach (var candidate in dto.Versions)
        {
            if (SpaceDockVersionParsing.TryNormalize(candidate.FriendlyVersion, out var parsed)
                && TryMapRelease(dto, candidate, out var release))
            {
                yield return (parsed, release, candidate);
            }
        }
    }

    /// <summary>
    /// Maps one SpaceDock version onto a release. An unusable version string,
    /// game version, release date, or download path yields no release; the
    /// listing itself stays visible either way.
    /// </summary>
    private static bool TryMapRelease(SpaceDockModDto dto, SpaceDockVersionDto version, [NotNullWhen(true)] out ModVersionMetadata? release)
    {
        release = null;

        if (!SpaceDockVersionParsing.TryNormalize(version.FriendlyVersion, out var modVersion))
            return false;

        if (!GameVersion.TryParse(version.RawGameVersion, out var gameVersion))
            return false;

        if (version.Created is not { } releaseDate)
            return false;

        if (string.IsNullOrWhiteSpace(version.DownloadPath))
            return false;

        // No checksum and no sizes: the API does not expose them.
        var download = new DownloadInfo(
            url: $"{BaseUrl}{version.DownloadPath}",
            sha256: null,
            sizeBytes: null,
            contentType: "application/zip");

        release = new ModVersionMetadata(
            specVersion: SpecVersions.Highest,
            modId: dto.Id.ToString(CultureInfo.InvariantCulture),
            version: modVersion,
            releaseStatus: modVersion.PreRelease is null ? ReleaseStatus.Stable : ReleaseStatus.Testing,
            releaseDate: releaseDate,
            gameMin: version.RawGameVersion,
            gameMinRevision: gameVersion.Revision,
            download: download,
            installSizeBytes: null,
            dependencies: Array.Empty<ModDependency>(),
            changelog: string.IsNullOrWhiteSpace(version.Changelog) ? null : version.Changelog,
            source: SourceName);

        return true;
    }

    private static bool IsKsaForumsUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && string.Equals(parsed.Host, KsaForumsHost, StringComparison.OrdinalIgnoreCase);
}

internal sealed class SpaceDockBrowseResponseDto
{
    public List<SpaceDockModDto> Result { get; set; } = new();
    public int Count { get; set; }
    public int Pages { get; set; }
    public int Page { get; set; }
}

internal sealed class SpaceDockModDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? License { get; set; }
    public int DefaultVersionId { get; set; }
    public List<SpaceDockVersionDto> Versions { get; set; } = new();
    public string? Updated { get; set; }
    public string? Url { get; set; }
    public string? Website { get; set; }
    public string? SourceCode { get; set; }

    // Nullable: not guaranteed on every endpoint; see IsKsaMod.
    public int? GameId { get; set; }
}

internal sealed class SpaceDockVersionDto
{
    public int Id { get; set; }
    public string FriendlyVersion { get; set; } = string.Empty;
    public string DownloadPath { get; set; } = string.Empty;
    public string? Changelog { get; set; }
    public DateTimeOffset? Created { get; set; }

    [JsonPropertyName("game_version")]
    public string RawGameVersion { get; set; } = string.Empty;
}

internal sealed class SpaceDockGameDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Short { get; set; }
}
