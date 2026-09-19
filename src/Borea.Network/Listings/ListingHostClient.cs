using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Network.SpaceDock;

namespace Borea.Network.Listings;

/// <summary>
/// Reads a GitHub repository without signing in, or a SpaceDock mod, and picks the latest release and its archive
/// the way the watcher of content-index-releases does.
/// </summary>
public sealed class ListingHostClient : IListingHostClient
{
    internal const string GitHubApi = "https://api.github.com";

    private const string ForumsHost = "forums.ahwoo.com";

    private const int KsaGameId = 22409;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _http;

    public ListingHostClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public Task<ListingHostFacts> ReadAsync(ListingSourceReference source, CancellationToken cancellationToken = default) => source switch
    {
        ListingSourceReference.GitHub github => ReadGitHubAsync(github, cancellationToken),
        ListingSourceReference.SpaceDock spaceDock => ReadSpaceDockAsync(spaceDock, cancellationToken),
        _ => throw new ArgumentException("The source names no host Borea reads.", nameof(source)),
    };

    private async Task<ListingHostFacts> ReadGitHubAsync(ListingSourceReference.GitHub source, CancellationToken cancellationToken)
    {
        var repository = await GetGitHubAsync<GitHubRepositoryDto>($"{GitHubApi}/repos/{source.FullName}", cancellationToken).ConfigureAwait(false)
            ?? throw new ListingSourceException($"GitHub has no repository '{source.FullName}' that Borea can read.");
        var releases = await GetGitHubAsync<List<GitHubReleaseDto>>($"{GitHubApi}/repos/{repository.FullName}/releases?per_page=100", cancellationToken).ConfigureAwait(false) ?? [];

        var links = new List<ListingLink>();
        if (Https(repository.Homepage) is { } homepage)
            links.Add(new ListingLink(IsForums(homepage) ? "forums" : "homepage", homepage));
        links.Add(new ListingLink("repository", repository.HtmlUrl));
        if (repository.HasIssues)
            links.Add(new ListingLink("bugtracker", $"{repository.HtmlUrl}/issues"));

        var latest = releases
            .Where(release => !release.Draft && Version(release.TagName) is not null)
            .OrderByDescending(release => release.PublishedAt ?? release.CreatedAt)
            .ThenByDescending(release => release.TagName, StringComparer.Ordinal)
            .FirstOrDefault();

        return new ListingHostFacts(
            source,
            repository.Name,
            repository.Description,
            repository.License?.SpdxId is { } spdx && spdx != "NOASSERTION" ? spdx : null,
            repository.Owner?.Type == "User" && !string.IsNullOrWhiteSpace(repository.Owner.Login) ? [repository.Owner.Login] : [],
            links,
            new ListingReleases(repository.FullName, null),
            latest is null ? null : GitHubRelease(latest, repository.Name));
    }

    /// <summary>One zip archive is the release. Of several, the one named after the listing wins, and otherwise none does.</summary>
    private static ListingHostRelease GitHubRelease(GitHubReleaseDto release, string listingId)
    {
        var uploaded = release.Assets.Where(asset => asset.State == "uploaded").ToList();
        var archives = uploaded.Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (archives.Count == 0)
            archives = uploaded.Where(asset => asset.ContentType is "application/zip" or "application/x-zip-compressed").ToList();

        var chosen = archives.Count <= 1 ? archives.FirstOrDefault() : null;
        if (archives.Count > 1)
        {
            var id = listingId.ToLowerInvariant();
            var tag = release.TagName.TrimStart('v', 'V').ToLowerInvariant();
            chosen = new[] { $"{id}.zip", $"{id}-{tag}.zip", $"{id}_{tag}.zip" }
                .Select(wanted => archives.FirstOrDefault(asset => asset.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(asset => asset is not null);
        }

        return new ListingHostRelease(
            release.TagName,
            Version(release.TagName)!,
            chosen?.BrowserDownloadUrl,
            chosen?.Size,
            chosen is null ? archives.Select(asset => asset.Name).ToList() : []);
    }

    private async Task<ListingHostFacts> ReadSpaceDockAsync(ListingSourceReference.SpaceDock source, CancellationToken cancellationToken)
    {
        SpaceDockModDto? mod;
        try
        {
            mod = await _http.GetFromJsonAsync<SpaceDockModDto>($"{SpaceDockModRepository.BaseUrl}/api/mod/{source.ModId}", Json, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            mod = null;
        }

        if (mod is null)
            throw new ListingSourceException($"SpaceDock has no mod {source.ModId}.");
        if (mod.GameId is { } game && game != KsaGameId)
            throw new ListingSourceException($"SpaceDock mod {source.ModId} is not a Kitten Space Agency mod.");

        var page = SpaceDockUrl(mod.Url) ?? $"{SpaceDockModRepository.BaseUrl}/mod/{mod.Id}";
        var links = new List<ListingLink>();
        if (Https(mod.Website) is { } website)
            links.Add(new ListingLink(IsForums(website) ? "forums" : "homepage", website));
        if (Https(mod.SourceCode) is { } sourceCode)
            links.Add(new ListingLink("repository", sourceCode));
        links.Add(new ListingLink("spacedock", page));

        var latest = mod.Versions
            .Where(version => Version(version.FriendlyVersion) is not null && !string.IsNullOrWhiteSpace(version.DownloadPath))
            .OrderByDescending(version => version.Created)
            .ThenByDescending(version => version.FriendlyVersion, StringComparer.Ordinal)
            .FirstOrDefault();

        return new ListingHostFacts(
            source,
            string.IsNullOrWhiteSpace(mod.Name) ? null : mod.Name,
            mod.ShortDescription,
            mod.License,
            string.IsNullOrWhiteSpace(mod.Author) ? [] : [mod.Author],
            links,
            new ListingReleases(null, mod.Id),
            latest is null
                ? null
                : new ListingHostRelease(latest.FriendlyVersion.Trim(), Version(latest.FriendlyVersion)!, SpaceDockUrl(latest.DownloadPath), null, []));
    }

    private async Task<T?> GetGitHubAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnavailableForLegalReasons)
            return default;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The SemVer version of a tag, with the leading v a tag may carry stripped, or null.</summary>
    private static string? Version(string? tag)
    {
        var value = tag?.Trim() ?? string.Empty;
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        return ModVersion.TryParse(value, out _) ? value : null;
    }

    private static string? Https(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri.AbsoluteUri : null;

    /// <summary>A path of the SpaceDock API as an address on SpaceDock itself, or null when it would lead anywhere else.</summary>
    private static string? SpaceDockUrl(string? path)
    {
        var host = new Uri(SpaceDockModRepository.BaseUrl);
        return !string.IsNullOrWhiteSpace(path)
            && path.StartsWith('/')
            && Uri.TryCreate(host, path, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && uri.UserInfo.Length == 0
            && string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase)
                ? uri.AbsoluteUri
                : null;
    }

    private static bool IsForums(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && string.Equals(uri.Host, ForumsHost, StringComparison.OrdinalIgnoreCase);

    private sealed class GitHubRepositoryDto
    {
        public string Name { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string HtmlUrl { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Homepage { get; set; }

        public bool HasIssues { get; set; }

        public GitHubOwnerDto? Owner { get; set; }

        public GitHubLicenseDto? License { get; set; }
    }

    private sealed class GitHubOwnerDto
    {
        public string Login { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    private sealed class GitHubLicenseDto
    {
        public string? SpdxId { get; set; }
    }

    private sealed class GitHubReleaseDto
    {
        public string TagName { get; set; } = string.Empty;

        public bool Draft { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public List<GitHubAssetDto> Assets { get; set; } = [];
    }

    private sealed class GitHubAssetDto
    {
        public string Name { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public string? ContentType { get; set; }

        public long? Size { get; set; }

        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
