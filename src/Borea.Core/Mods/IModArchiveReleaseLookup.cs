namespace Borea.Core.Mods;

public interface IModArchiveReleaseLookup
{
    Task<ModVersionMetadata?> FindBySha256Async(string modId, string sha256, CancellationToken cancellationToken = default);
}
