using Borea.Core.Listings;

namespace Borea.Core.Logging;

public sealed class LoggingListingPublisher : IListingPublisher
{
    private readonly IBoreaLog _log;

    public IListingPublisher Inner { get; }

    public LoggingListingPublisher(IListingPublisher inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<ListingOwnership> CheckOwnershipAsync(ListingDraft submitted, ListingDraft? listed, CancellationToken cancellationToken = default) =>
        Inner.CheckOwnershipAsync(submitted, listed, cancellationToken);

    public async Task<ListingPullRequest> PublishAsync(ListingSubmission submission, IProgress<ListingPublishStep>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var pullRequest = await Inner.PublishAsync(submission, progress, cancellationToken).ConfigureAwait(false);
            if (pullRequest.Outcome != ListingPublishOutcome.Unchanged)
                _log.Write($"{(pullRequest.Outcome == ListingPublishOutcome.Opened ? "Opened" : "Updated")} listing pull request {pullRequest.Url.AbsoluteUri}");

            return pullRequest;
        }
        catch (ListingPublishException exception)
        {
            _log.Write($"Listing pull request of {submission.Id} failed. {exception.Message}");
            throw;
        }
    }

    public Task<ListingPullRequestStatus> GetStatusAsync(int number, CancellationToken cancellationToken = default) =>
        Inner.GetStatusAsync(number, cancellationToken);
}
