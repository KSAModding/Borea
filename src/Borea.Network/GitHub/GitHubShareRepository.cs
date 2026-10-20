using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Borea.Core.GitHub;
using Borea.Core.Mods;
using Borea.Core.Sharing;

namespace Borea.Network.GitHub;

/// <summary>
/// IShareRepository through the signed-in GitHub session. Creating the repository needs the Administration permission of the App
/// on the user, so GitHub refuses it while the App does not have it, and the player can make the repository by hand instead.
/// </summary>
public sealed class GitHubShareRepository : IShareRepository
{
    private const string RepositoryName = ShareRepository.Name;

    private const string Api = GitHubApi.Root;

    private readonly IGitHubSession _session;
    private readonly GitHubApi _api;

    public GitHubShareRepository(IGitHubSession session, HttpClient http, TimeProvider? time = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _api = new GitHubApi(session, http, time);
    }

    public async Task<ShareRepository> EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (_session.State.Login is not { } login)
            throw new ShareRepositoryException(ShareRepositoryFailure.SignedOut);

        return await RunAsync(null, async () =>
        {
            var existing = await _api.SendAsync(HttpMethod.Get, $"{Api}/repos/{login}/{RepositoryName}", null, cancellationToken).ConfigureAwait(false);
            if (existing.Status != HttpStatusCode.NotFound)
                return Repository(GitHubApi.Parse<RepositoryDto>(GitHubApi.Ensure(existing)));

            // A release needs a commit to tag, so the repository starts with one.
            var body = new { name = RepositoryName, description = "Vehicles and saves shared from Borea.", auto_init = true };
            var created = await _api.SendAsync(HttpMethod.Post, $"{Api}/user/repos", body, cancellationToken).ConfigureAwait(false);
            if (created.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound && created.RetryAt is null)
                throw new ShareRepositoryException(ShareRepositoryFailure.MayNotCreateRepository, created.Message);

            return Repository(GitHubApi.Parse<RepositoryDto>(GitHubApi.Ensure(created)));
        }).ConfigureAwait(false);
    }

    public Task<string> NextVersionAsync(ShareRepository repository, string tagPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrEmpty(tagPrefix);
        return RunAsync(repository, async () =>
        {
            ModVersion? highest = null;
            await foreach (var release in _api.GetPagesAsync<ReleaseDto>($"{Api}/repos/{repository.FullName}/releases", cancellationToken).ConfigureAwait(false))
            {
                if (release.TagName?.StartsWith(tagPrefix, StringComparison.Ordinal) == true
                    && ModVersion.TryParse(release.TagName[tagPrefix.Length..], out var version)
                    && (highest is not { } current || version.CompareTo(current) > 0))
                {
                    highest = version;
                }
            }

            return highest is { } last ? string.Create(CultureInfo.InvariantCulture, $"{last.Major}.{last.Minor + 1}.0") : "1.0.0";
        });
    }

    public Task<Uri> PublishAsync(ShareRepository repository, string tagPrefix, string version, string assetName, Stream archive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(archive);
        return RunAsync(repository, async () =>
        {
            var tag = tagPrefix + version;
            var reply = await _api.SendAsync(HttpMethod.Post, $"{Api}/repos/{repository.FullName}/releases", new { tag_name = tag, name = tag }, cancellationToken).ConfigureAwait(false);
            if (reply.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound && reply.RetryAt is null)
                throw new ShareRepositoryException(ShareRepositoryFailure.AppNotOnRepository, reply.Message, repository.Id);

            var release = GitHubApi.Parse<ReleaseDto>(GitHubApi.Ensure(reply));
            if (release.HtmlUrl is not { } page || !Uri.TryCreate(UploadBase(release.UploadUrl), UriKind.Absolute, out var upload) || upload.Host != "uploads.github.com")
                throw new ShareRepositoryException(ShareRepositoryFailure.UnexpectedResponse);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{upload.AbsoluteUri}?name={Uri.EscapeDataString(assetName)}")
                {
                    Content = new StreamContent(archive),
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                using var response = await _session.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new ShareRepositoryException(response.StatusCode == HttpStatusCode.Unauthorized ? ShareRepositoryFailure.SignedOut : ShareRepositoryFailure.UnexpectedResponse, $"HTTP {(int)response.StatusCode}");
            }
            catch (Exception exception) when (exception is ShareRepositoryException or HttpRequestException or OperationCanceledException)
            {
                // The watcher rejects a release without its one asset, and the tag would block the version.
                await DeleteReleaseAsync(repository, release.Id).ConfigureAwait(false);
                throw;
            }

            return new Uri(page);
        });
    }

    private async Task DeleteReleaseAsync(ShareRepository repository, long id)
    {
        try
        {
            await _api.SendAsync(HttpMethod.Delete, $"{Api}/repos/{repository.FullName}/releases/{id.ToString(CultureInfo.InvariantCulture)}", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (GitHubApiException)
        {
        }
    }

    /// <summary>The upload_url of a release without its URI template part.</summary>
    private static string UploadBase(string? template) => template is null ? string.Empty : template.Split('{')[0];

    private static ShareRepository Repository(RepositoryDto dto) =>
        dto.FullName is { Length: > 0 } name ? new ShareRepository(name, dto.Id) : throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);

    private static async Task<T> RunAsync<T>(ShareRepository? repository, Func<Task<T>> work)
    {
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (GitHubApiException exception)
        {
            var failure = exception.Failure switch
            {
                GitHubApiFailure.SignedOut => ShareRepositoryFailure.SignedOut,
                GitHubApiFailure.RateLimited => ShareRepositoryFailure.RateLimited,
                GitHubApiFailure.NetworkError => ShareRepositoryFailure.NetworkError,
                GitHubApiFailure.Refused or GitHubApiFailure.Forbidden => ShareRepositoryFailure.Refused,
                _ => ShareRepositoryFailure.UnexpectedResponse,
            };
            throw new ShareRepositoryException(failure, exception.Detail, repository?.Id, exception);
        }
    }

    private sealed class RepositoryDto
    {
        public long Id { get; set; }

        public string? FullName { get; set; }
    }

    private sealed class ReleaseDto
    {
        public long Id { get; set; }

        public string? TagName { get; set; }

        public string? HtmlUrl { get; set; }

        public string? UploadUrl { get; set; }
    }
}
