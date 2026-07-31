using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Network.SpaceDock;

/// <summary>
/// IModRepository implementation against SpaceDock's public API.
/// ModId returned here is a placeholder: SpaceDock's own numeric mod ID,
/// stringified.
/// </summary>
public sealed class SpaceDockModRepository : IModRepository
{
    // SpaceDock's internal database ID for KSA.
    private const int KsaGameId = 22409;

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
        // Single page for now — SpaceDock's /api/browse is paginated (up to
        // 500/page) and IModRepository has no paging concept. Revisit if
        // KSA's catalog grows past one page's worth of results.
        var response = await _httpClient.GetFromJsonAsync<SpaceDockBrowseResponseDto>(
            $"api/browse?game_id={KsaGameId}&count=500", JsonOptions, cancellationToken).ConfigureAwait(false);

        return (response?.Result ?? new()).Select(TryMapToMetadata).Where(m => m is not null).Select(m => m!).ToList();
    }

    public async Task<ModMetadata?> GetLatestAsync(string modId, CancellationToken cancellationToken = default)
    {
        if (!TryResolveSpaceDockId(modId, out var spaceDockId))
            return null;

        var dto = await _httpClient.GetFromJsonAsync<SpaceDockModDto>(
            $"api/mod/{spaceDockId}", JsonOptions, cancellationToken).ConfigureAwait(false);

        return dto is null ? null : TryMapToMetadata(dto);
    }

    public async Task<ModMetadata?> GetVersionAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
    {
        if (!TryResolveSpaceDockId(modId, out var spaceDockId))
            return null;

        var dto = await _httpClient.GetFromJsonAsync<SpaceDockModDto>(
            $"api/mod/{spaceDockId}", JsonOptions, cancellationToken).ConfigureAwait(false);

        if (dto is null || !IsKsaMod(dto))
            return null;

        var matchingVersion = dto.Versions.FirstOrDefault(v =>
            TryNormalizeModVersion(v.FriendlyVersion, out var parsed) && parsed.Equals(version));

        return matchingVersion is null ? null : TryMapToMetadata(dto, matchingVersion);
    }

    public async Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
    {
        if (!TryResolveSpaceDockId(modId, out var spaceDockId))
            return Array.Empty<ModVersion>();

        var dto = await _httpClient.GetFromJsonAsync<SpaceDockModDto>(
            $"api/mod/{spaceDockId}", JsonOptions, cancellationToken).ConfigureAwait(false);

        if (dto is null || !IsKsaMod(dto))
            return Array.Empty<ModVersion>();

        return dto.Versions
            .Select(v => TryNormalizeModVersion(v.FriendlyVersion, out var parsed) ? (ModVersion?)parsed : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
    }

    public async Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        // Live results include game_id directly per mod (undocumented in
        // api.md, confirmed by real response) — IsKsaMod filters on it.
        var results = await _httpClient.GetFromJsonAsync<List<SpaceDockModDto>>(
            $"api/search/mod?query={Uri.EscapeDataString(query)}", JsonOptions, cancellationToken).ConfigureAwait(false);

        return (results ?? new())
            .Where(IsKsaMod)
            .Select(TryMapToMetadata)
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();
    }

    private bool TryResolveSpaceDockId(string modId, out int spaceDockId)
    {
        // A raw integer means "still the browse-time placeholder" — use directly.
        if (int.TryParse(modId, out spaceDockId))
            return true;

        // Otherwise this is the true, mod.toml-confirmed ModId — ask the resolver.
        return _resolver.TryResolve(modId, out spaceDockId);
    }

    /// <summary>
    /// True if this listing is for KSA. Primary check is the GameId field
    /// when present (confirmed present on search results; unconfirmed on
    /// browse/mod-detail, so treated as optional here). GameId == 0 (unset/
    /// missing on this endpoint) falls back to no game_id, in which case
    /// TryMapToMetadata's GameVersion parsing acts as the fallback filter.
    /// </summary>
    private static bool IsKsaMod(SpaceDockModDto dto) => dto.GameId is null or KsaGameId;

    private static ModMetadata? TryMapToMetadata(SpaceDockModDto dto)
    {
        if (!IsKsaMod(dto))
            return null;

        var defaultVersion = dto.Versions.FirstOrDefault(v => v.Id == dto.DefaultVersionId);
        var ordered = defaultVersion is null
            ? dto.Versions
            : new[] { defaultVersion }.Concat(dto.Versions.Where(v => v != defaultVersion));

        foreach (var candidate in ordered)
        {
            var mapped = TryMapToMetadata(dto, candidate);
            if (mapped is not null)
                return mapped;
        }

        return null;
    }

    private static ModMetadata? TryMapToMetadata(SpaceDockModDto dto, SpaceDockVersionDto version)
    {
        if (!TryNormalizeModVersion(version.FriendlyVersion, out var modVersion))
            return null;

        if (!GameVersion.TryParse(version.RawGameVersion, out var gameVersion))
            return null;

        var releasedAt = version.Created ?? DateTimeOffset.UtcNow;
        var homepageUrl = !string.IsNullOrWhiteSpace(dto.Website) ? dto.Website
            : !string.IsNullOrWhiteSpace(dto.Url) ? $"https://spacedock.info{dto.Url}"
            : null;

        return new ModMetadata(
            dto.Id.ToString(CultureInfo.InvariantCulture),
            source: "spacedock",
            name: dto.Name,
            author: dto.Author,
            version: modVersion,
            builtForGameVersion: gameVersion,
            description: dto.Description ?? dto.ShortDescription ?? string.Empty,
            releasedAt: releasedAt,
            fileSizeBytes: 0, // Still not exposed anywhere in the API.
            homepageUrl: homepageUrl,
            changeLog: version.Changelog);
    }

    /// <summary>
    /// Best-effort coercion of SpaceDock's free-text friendly_version into
    /// ModVersion's strict Major.Minor.Patch shape. Strips a leading 'v',
    /// pads missing components with 0.
    /// </summary>
    private static bool TryNormalizeModVersion(string raw, out ModVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        var parts = trimmed.Split('.');
        if (parts.Length >= 3 && parts.Take(3).All(p => int.TryParse(p, out _)))
            return ModVersion.TryParse($"{parts[0]}.{parts[1]}.{parts[2]}", out version);

        if (parts.Length == 2 && parts.All(p => int.TryParse(p, out _)))
            return ModVersion.TryParse($"{parts[0]}.{parts[1]}.0", out version);

        if (parts.Length == 1 && int.TryParse(parts[0], out _))
            return ModVersion.TryParse($"{parts[0]}.0.0", out version);

        return false;
    }
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
    public int DefaultVersionId { get; set; }
    public List<SpaceDockVersionDto> Versions { get; set; } = new();
    public string? Updated { get; set; }
    public string? Url { get; set; }
    public string? Website { get; set; }

    // Confirmed present on /api/search/mod results; unconfirmed elsewhere,
    // hence nullable — see IsKsaMod's fallback behavior.
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