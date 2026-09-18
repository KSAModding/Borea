namespace Borea.Core.Announcements;

public interface IAnnouncementReader
{
    /// <summary>Reads the announcements file at <paramref name="path"/>. A missing file has no posts, and a file that is not an announcements file throws <see cref="InvalidDataException"/>.</summary>
    Task<AnnouncementFile> ReadAsync(string path, CancellationToken cancellationToken = default);
}
