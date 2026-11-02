namespace Borea.Core.Mods;

public interface IModArchiveReleaseLookup
{
    /// <summary>The one release of the mod with an archive of these bytes, which takes that archive as its <see cref="ModVersionMetadata.Download"/>.</summary>
    Task<ModVersionMetadata?> FindBySha256Async(string modId, string sha256, CancellationToken cancellationToken = default);
}
