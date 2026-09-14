using Borea.Core.Index;

namespace Borea.Core.Logging;

public sealed class LoggingContentIndexFetcher : IContentIndexFetcher
{
    private readonly IBoreaLog _log;

    public IContentIndexFetcher Inner { get; }

    public LoggingContentIndexFetcher(IContentIndexFetcher inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default)
    {
        try
        {
            var result = await Inner.FetchAsync(destinationPath, ct).ConfigureAwait(false);
            _log.Write(result == ContentIndexFetchResult.NotModified
                ? "Index fetch: not modified, the ETag matched."
                : "Index fetch: downloaded a new snapshot.");
            return result;
        }
        catch (OperationCanceledException)
        {
            _log.Write("Index fetch cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"Index fetch failed: {exception.GetType().Name}: {exception.Message}");
            throw;
        }
    }
}
