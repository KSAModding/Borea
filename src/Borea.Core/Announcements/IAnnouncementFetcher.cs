namespace Borea.Core.Announcements;

public interface IAnnouncementFetcher
{
    /// <summary>
    /// Downloads the announcements file to <paramref name="destinationPath"/> when it changed, and returns false when the ETag matched.
    /// A download the reader rejects throws <see cref="InvalidDataException"/> and leaves the cached file as it was.
    /// </summary>
    Task<bool> FetchAsync(string destinationPath, CancellationToken cancellationToken = default);
}
