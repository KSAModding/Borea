namespace Borea.Core.Mods;

public interface IForeignModReleaseMatcher
{
    /// <summary>
    /// Adopts the newest release of the folder's listing in the content index, yanked releases included, whose files are all in the folder with the same content. Returns null when no release matches.
    /// </summary>
    Task<ForeignModAdoptionResult?> AdoptMatchingReleaseAsync(
        Guid instanceId,
        string folderName,
        CancellationToken cancellationToken = default);
}
