using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Borea.Core.Updates;

namespace Borea.Network.GitHub;

/// <summary>
/// IBoreaReleaseCheck against the GitHub "latest release" endpoint, which allows 60 unauthenticated
/// requests an hour, so callers ask once.
/// </summary>
public sealed class BoreaReleaseCheck : IBoreaReleaseCheck
{
    internal const string LatestReleaseUrl = "https://api.github.com/repos/KSAModding/Borea/releases/latest";

    internal const string ApiVersion = "2026-03-10";

    private const string ReleasePagePathPrefix = "/KSAModding/Borea/releases/";

    private readonly HttpClient _httpClient;

    public BoreaReleaseCheck(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<BoreaRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        LatestReleaseDto? dto;
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            // 404 means no release yet, 403 or 429 a rate limit.
            if (!response.IsSuccessStatusCode)
                return null;

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            dto = JsonSerializer.Deserialize<LatestReleaseDto>(text);
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

        if (dto is null || dto.Draft || dto.Prerelease)
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

        return new BoreaRelease(version, dto.TagName!, page.AbsoluteUri);
    }
}

internal sealed class LatestReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
}
