namespace Borea.Core.ModPacks;

/// <summary>
/// Tracks which mod packs the user has favorited/bookmarked for quick
/// access.
/// </summary>
public interface IModPackFavoritesRepository
{
    Task<IReadOnlyList<string>> GetFavoriteModPackIdsAsync(CancellationToken cancellationToken = default);

    Task AddFavoriteAsync(string modPackId, CancellationToken cancellationToken = default);

    Task RemoveFavoriteAsync(string modPackId, CancellationToken cancellationToken = default);

    Task<bool> IsFavoriteAsync(string modPackId, CancellationToken cancellationToken = default);
}