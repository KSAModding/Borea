using System.Net;
using Borea.Core.GitHub;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Network.GitHub;
using Borea.Network.Images;

namespace Borea.Network.Listings;

/// <summary>
/// Fetches a hosted image with the handler and rules of the listing images, and measures its bytes. For an address on a branch it asks
/// the REST API of GitHub, without the token, for the commit of the branch.
/// </summary>
public sealed class ListingImageMeasurer : IListingImageMeasurer, IDisposable
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long the commit of a branch is used again, so the images of one listing on the same branch ask GitHub once.</summary>
    private static readonly TimeSpan BranchCommitLifetime = TimeSpan.FromMinutes(1);

    /// <summary>How many branch names a refs/heads/ address tries, so a deep path costs few requests of the rate limit.</summary>
    private const int MaxBranchNames = 3;

    private readonly ContentImageFetcher _fetcher;
    private readonly GitHubApi _api;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, (Task<BranchAnswer> Answer, DateTimeOffset At)> _branchCommits = new(StringComparer.Ordinal);
    private DateTimeOffset _rateLimitedUntil = DateTimeOffset.MinValue;

    /// <param name="http">Sends the requests to the GitHub API.</param>
    /// <param name="imageHandler">Fetches the images. Null builds the handler of the listing images.</param>
    public ListingImageMeasurer(IGitHubSession session, HttpClient http, HttpMessageHandler? imageHandler = null)
        : this(imageHandler ?? ContentImageFetcher.CreateHandler(), FetchTimeout, session, http)
    {
    }

    internal ListingImageMeasurer(HttpMessageHandler imageHandler, TimeSpan timeout, IGitHubSession session, HttpClient http, TimeProvider? time = null)
    {
        _fetcher = new ContentImageFetcher(imageHandler, timeout);
        _time = time ?? TimeProvider.System;
        _api = new GitHubApi(session, http, _time);
    }

    public async Task<ListingImageMeasurement> MeasureAsync(string url, ListingImageRole role, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return ListingImageMeasurement.Failed($"{url} is not an https URL");

        var result = await _fetcher.FetchAsync(uri.AbsoluteUri, ListingImageMeasurement.MaxBytes(role), ContentImageResult.Loaded, cancellationToken).ConfigureAwait(false);
        if (result.IsLoaded)
            return ListingImageMeasurement.Of(result.Bytes.Span, role);

        return result.Failure == ContentImageFailure.Unavailable
            ? ListingImageMeasurement.Unavailable(result.Reason!)
            : ListingImageMeasurement.Failed(result.Reason!);
    }

    public async Task<ListingImageCheck> CheckAsync(string url, ListingImageRole role, CancellationToken cancellationToken = default)
    {
        ListingImageBranch? branch = null;
        if (GitHubBranchAddress.Of(url) is { } address)
        {
            var (found, answer) = await FindBranchAsync(address, fresh: false, cancellationToken).ConfigureAwait(false);
            if (answer.Commit is { } commit)
            {
                // The file at the commit the branch points to is the current file of the branch, so this one read checks the record
                // and also proves the commit address. It also avoids an old copy that the raw host can still serve for the branch.
                var commitUrl = found.AtCommit(commit);
                var atCommit = await MeasureAsync(commitUrl, role, cancellationToken).ConfigureAwait(false);
                return new ListingImageCheck(atCommit, new ListingImageBranch(found.Branch, atCommit.IsMeasured ? commitUrl : null));
            }

            if (address.IsSure)
                branch = new ListingImageBranch(address.CertainBranch, null);
        }

        return new ListingImageCheck(await MeasureAsync(url, role, cancellationToken).ConfigureAwait(false), branch);
    }

    public async Task<ListingImageBranch?> FindCommitAddressAsync(string url, ListingImageRole role, string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        if (GitHubBranchAddress.Of(url) is not { } address)
            return null;

        // The author reads the image after a change, so the branch is asked again instead of from the cache.
        var (found, answer) = await FindBranchAsync(address, fresh: true, cancellationToken).ConfigureAwait(false);
        if (answer.Commit is not { } commit)
            return address.IsSure ? new ListingImageBranch(address.CertainBranch, null) : null;

        // Only a file with the same bytes is offered, so the suggestion never changes the image the record describes.
        var commitUrl = found.AtCommit(commit);
        var measured = await MeasureAsync(commitUrl, role, cancellationToken).ConfigureAwait(false);
        return new ListingImageBranch(found.Branch, string.Equals(measured.Sha256, sha256, StringComparison.OrdinalIgnoreCase) ? commitUrl : null);
    }

    /// <summary>
    /// Asks GitHub for the branch of the address. When GitHub has no such branch, a refs/heads/ address tries the next longer name, such
    /// as release/1.0, up to <see cref="MaxBranchNames"/> names.
    /// </summary>
    private async Task<(GitHubBranchAddress Address, BranchAnswer Answer)> FindBranchAsync(GitHubBranchAddress address, bool fresh, CancellationToken cancellationToken)
    {
        for (var names = 1; ; names++)
        {
            var answer = await BranchCommitAsync(address, fresh, cancellationToken).ConfigureAwait(false);
            if (!answer.Missing || names == MaxBranchNames || address.Longer() is not { } longer)
                return (address, answer);

            address = longer;
        }
    }

    /// <summary>
    /// The commit the branch points to, whether GitHub has no such branch, or neither when GitHub did not answer. A read that another caller
    /// started and then canceled is read again, because that cancel is not this caller's.
    /// </summary>
    private async Task<BranchAnswer> BranchCommitAsync(GitHubBranchAddress address, bool fresh, CancellationToken cancellationToken)
    {
        try
        {
            return await SharedBranchCommitAsync(address, fresh, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await SharedBranchCommitAsync(address, fresh, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The read of the branch that callers share for <see cref="BranchCommitLifetime"/>, with the token of the caller that started it.</summary>
    private Task<BranchAnswer> SharedBranchCommitAsync(GitHubBranchAddress address, bool fresh, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var key = $"{address.Owner}/{address.Repository}/{address.Branch}".ToUpperInvariant();
        lock (_branchCommits)
        {
            if (now < _rateLimitedUntil)
                return Task.FromResult(default(BranchAnswer));

            if (!fresh && _branchCommits.TryGetValue(key, out var known) && now - known.At < BranchCommitLifetime && !known.Answer.IsCanceled)
                return known.Answer;

            var answer = ReadBranchCommitAsync(address, cancellationToken);
            _branchCommits[key] = (answer, now);
            return answer;
        }
    }

    private async Task<BranchAnswer> ReadBranchCommitAsync(GitHubBranchAddress address, CancellationToken cancellationToken)
    {
        var branch = string.Join('/', address.Branch.Split('/').Select(Uri.EscapeDataString));
        var url = $"{GitHubApi.Root}/repos/{Uri.EscapeDataString(address.Owner)}/{Uri.EscapeDataString(address.Repository)}/branches/{branch}";
        try
        {
            var reply = await _api.SendAsync(HttpMethod.Get, url, null, cancellationToken, anonymous: true).ConfigureAwait(false);
            if (reply.Status == HttpStatusCode.NotFound)
                return new BranchAnswer(null, Missing: true);

            var sha = GitHubApi.Parse<BranchDto>(GitHubApi.Ensure(reply)).Commit?.Sha;
            return new BranchAnswer(sha is not null && GitHubBranchAddress.IsCommit(sha) ? sha : null, Missing: false);
        }
        catch (GitHubApiException exception)
        {
            if (exception.RetryAt is { } retryAt)
            {
                lock (_branchCommits)
                    _rateLimitedUntil = retryAt;
            }

            return default;
        }
    }

    public void Dispose() => _fetcher.Dispose();

    /// <param name="Commit">The commit the branch points to, or null.</param>
    /// <param name="Missing">True when GitHub answered that the repository has no such branch.</param>
    private readonly record struct BranchAnswer(string? Commit, bool Missing);

    private sealed class BranchDto
    {
        public BranchCommitDto? Commit { get; set; }
    }

    private sealed class BranchCommitDto
    {
        public string? Sha { get; set; }
    }
}
