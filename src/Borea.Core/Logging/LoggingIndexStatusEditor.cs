using Borea.Core.Stewardship;

namespace Borea.Core.Logging;

public sealed class LoggingIndexStatusEditor : IIndexStatusEditor
{
    private readonly IBoreaLog _log;

    public IIndexStatusEditor Inner { get; }

    public LoggingIndexStatusEditor(IIndexStatusEditor inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<IndexStatusOverview> ReadAsync(CancellationToken cancellationToken = default) =>
        Inner.ReadAsync(cancellationToken);

    public Task<IndexStatusCheck> CheckAsync(IndexStatusChange change, CancellationToken cancellationToken = default) =>
        Inner.CheckAsync(change, cancellationToken);

    public Task<IReadOnlyList<string>> OwnedAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
        Inner.OwnedAsync(ids, cancellationToken);

    public async Task<IndexStatusPullRequest> OpenAsync(IndexStatusChange change, CancellationToken cancellationToken = default)
    {
        try
        {
            var pullRequest = await Inner.OpenAsync(change, cancellationToken).ConfigureAwait(false);
            _log.Write($"Opened index status pull request {pullRequest.Url.AbsoluteUri}: {change.Title}");
            return pullRequest;
        }
        catch (Exception exception) when (exception is StewardException or IndexStatusRefusedException)
        {
            _log.Write($"Index status pull request \"{change.Title}\" failed. {exception.Message}");
            throw;
        }
    }
}
