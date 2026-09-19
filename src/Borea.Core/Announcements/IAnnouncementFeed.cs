namespace Borea.Core.Announcements;

public interface IAnnouncementFeed
{
    /// <summary>Fetches the announcements and returns the posts of the cached file. A failed fetch or a broken file keeps the cached file.</summary>
    Task<IReadOnlyList<Announcement>> GetPostsAsync(CancellationToken cancellationToken = default);
}
