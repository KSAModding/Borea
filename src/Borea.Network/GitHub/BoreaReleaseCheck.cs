using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Borea.Core.Updates;

namespace Borea.Network.GitHub;

/// <summary>
/// IBoreaReleaseCheck against the GitHub releases list, which allows 60 unauthenticated
/// requests an hour, so callers ask once. Each check sends one request.
/// </summary>
public sealed class BoreaReleaseCheck : IBoreaReleaseCheck
{
    internal const string ReleasesUrl = "https://api.github.com/repos/KSAModding/Borea/releases?per_page=100";

    internal const string ApiVersion = "2026-03-10";

    private const string ReleasePagePathPrefix = "/KSAModding/Borea/releases/";

    /// <summary>Where the release files of this repository are served from. Only these URLs are fetched.</summary>
    internal const string DownloadUrlPrefix = "https://github.com/KSAModding/Borea/releases/download/";

    private readonly HttpClient _httpClient;

    public BoreaReleaseCheck(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<BoreaRelease>> GetReleasesAsync(BoreaUpdateChannel channel = BoreaUpdateChannel.Stable, CancellationToken cancellationToken = default)
    {
        var releases = await GetAsync<List<ReleaseDto?>>(ReleasesUrl, cancellationToken).ConfigureAwait(false);
        if (releases is null)
            return [];

        return releases
            .Select(dto => ToRelease(dto, allowPreRelease: channel != BoreaUpdateChannel.Stable))
            .OfType<BoreaRelease>()
            .Where(release => channel.Includes(release.Version))
            .DistinctBy(release => release.Version)
            .OrderByDescending(release => release.Version)
            .ToList();
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            // 403 or 429 is a rate limit, 404 a missing repository.
            if (!response.IsSuccessStatusCode)
                return null;

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // a timeout or a disposed client, not the caller's cancellation
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static BoreaRelease? ToRelease(ReleaseDto? dto, bool allowPreRelease)
    {
        if (dto is null || dto.Draft || (dto.Prerelease && !allowPreRelease))
            return null;

        if (!BoreaRelease.TryParseTag(dto.TagName, out var version))
            return null;

        // Only a release page of this repository is opened.
        if (!Uri.TryCreate(dto.HtmlUrl, UriKind.Absolute, out var page)
            || page.Scheme != Uri.UriSchemeHttps
            || !string.Equals(page.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !page.AbsolutePath.StartsWith(ReleasePagePathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new BoreaRelease(version, dto.TagName!, page.AbsoluteUri, string.IsNullOrWhiteSpace(dto.Body) ? null : dto.Body, dto.PublishedAt)
        {
            Assets = dto.Assets?.Select(ToAsset).OfType<BoreaReleaseAsset>().ToList() ?? [],
        };
    }

    /// <summary>
    /// A published file of this repository. A name that is not a plain file name or a URL that is not a
    /// release download of this repository is dropped, so nothing else is ever fetched or written.
    /// </summary>
    private static BoreaReleaseAsset? ToAsset(AssetDto? dto)
    {
        if (dto?.Name is not { Length: > 0 } name
            || name != Path.GetFileName(name)
            || name is "." or "..")
        {
            return null;
        }

        var url = dto.BrowserDownloadUrl;
        return url is not null && url.StartsWith(DownloadUrlPrefix, StringComparison.Ordinal)
            ? new BoreaReleaseAsset(name, url, dto.Size < 0 ? 0 : dto.Size)
            : null;
    }
}

internal sealed class ReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<AssetDto?>? Assets { get; set; }
}

internal sealed class AssetDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
