namespace Borea.Core.Mods;

/// <summary>
/// Tracks which mods the user has favorited/bookmarked for quick access.
/// </summary>
public interface IModFavoritesRepository
{
    Task<IReadOnlyList<string>> GetFavoriteModIdsAsync(CancellationToken cancellationToken = default);

    Task AddFavoriteAsync(string modId, CancellationToken cancellationToken = default);

    Task RemoveFavoriteAsync(string modId, CancellationToken cancellationToken = default);

    Task<bool> IsFavoriteAsync(string modId, CancellationToken cancellationToken = default);
}