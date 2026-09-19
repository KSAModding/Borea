using Borea.Core.Logging;
using Borea.Core.Paths;

namespace Borea.Core.Announcements;

public sealed class AnnouncementFeed : IAnnouncementFeed
{
    private readonly IAnnouncementFetcher _fetcher;
    private readonly IAnnouncementReader _reader;
    private readonly IGamePathProvider _paths;
    private readonly IBoreaLog _log;

    public AnnouncementFeed(IAnnouncementFetcher fetcher, IAnnouncementReader reader, IGamePathProvider paths, IBoreaLog log)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<IReadOnlyList<Announcement>> GetPostsAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.GetAnnouncementsPath();
        try
        {
            var downloaded = await _fetcher.FetchAsync(path, cancellationToken).ConfigureAwait(false);
            _log.Write(downloaded ? "Announcements fetch: downloaded a new file." : "Announcements fetch: not modified, the ETag matched.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _log.Write($"Announcements fetch failed, the cached file stays: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            var file = await _reader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            foreach (var reason in file.SkippedPosts)
                _log.Write($"Announcements: skipped a post. {reason}");
            return file.Posts;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _log.Write($"Announcements: the cached file cannot be read: {exception.GetType().Name}: {exception.Message}");
            return [];
        }
    }
}
