namespace Borea.Core.Mods;

/// <summary>
/// Supplies IDs that a source owns even when their metadata must not be shown
/// or used. Earlier claims prevent later sources from restoring that content.
/// </summary>
public interface IModIdClaimSource
{
    Task<IReadOnlyList<string>> GetClaimedModIdsAsync(CancellationToken cancellationToken = default);
}
