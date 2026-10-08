using Borea.Core.Stewardship;

namespace Borea.Core.Logging;

public sealed class LoggingReleaseAmendments : IReleaseAmendments
{
    private readonly IBoreaLog _log;

    public IReleaseAmendments Inner { get; }

    public LoggingReleaseAmendments(IReleaseAmendments inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<IReadOnlyList<string>> ReleasesAsync(string listingId, CancellationToken cancellationToken = default) =>
        Inner.ReleasesAsync(listingId, cancellationToken);

    public Task<ReleaseFiles> ReleaseFilesAsync(string listingId, CancellationToken cancellationToken = default) =>
        Inner.ReleaseFilesAsync(listingId, cancellationToken);

    public Task<ReleaseAmendmentPreview> PreviewAsync(ReleaseAmendmentRequest request, CancellationToken cancellationToken = default) =>
        Inner.PreviewAsync(request, cancellationToken);

    public async Task<ReleaseAmendmentPullRequest> OpenAsync(ReleaseAmendmentPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        try
        {
            var pullRequest = await Inner.OpenAsync(preview, cancellationToken).ConfigureAwait(false);
            _log.Write($"Opened release amendment pull request {pullRequest.Url.AbsoluteUri}: {pullRequest.Title}");
            return pullRequest;
        }
        catch (Exception exception) when (exception is StewardException or ReleaseAmendmentRefusedException or ReleaseAmendmentChangedException)
        {
            _log.Write($"Release amendment pull request \"{preview.Title}\" failed. {exception.Message}");
            throw;
        }
    }
}
