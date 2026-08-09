using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Toml;

namespace Borea.Storage.Mods;

/// <summary>
/// File-backed implementation of IModFavoritesRepository. Stores only bare
/// ModIds, per IModFavoritesRepository's own contract.
/// </summary>
public sealed class FileModFavoritesRepository : IModFavoritesRepository
{
    private readonly IGamePathProvider _pathProvider;

    public FileModFavoritesRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<IReadOnlyList<string>> GetFavoriteModIdsAsync(CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return dto.ModIds;
    }

    public async Task<bool> IsFavoriteAsync(string modId, CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return dto.ModIds.Contains(modId, ModIds.Comparer);
    }

    public async Task AddFavoriteAsync(string modId, CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync(cancellationToken).ConfigureAwait(false);

        if (dto.ModIds.Contains(modId, ModIds.Comparer))
            return;

        dto.ModIds.Add(modId);
        await WriteAsync(dto, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveFavoriteAsync(string modId, CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync(cancellationToken).ConfigureAwait(false);

        if (dto.ModIds.RemoveAll(id => ModIds.Equals(id, modId)) > 0)
            await WriteAsync(dto, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModFavoritesDto> ReadAsync(CancellationToken cancellationToken)
    {
        var path = _pathProvider.GetModFavoritesPath();
        var dto = await TomlFileStore.ReadAsync<ModFavoritesDto>(path, cancellationToken).ConfigureAwait(false);
        return dto ?? new ModFavoritesDto();
    }

    private Task WriteAsync(ModFavoritesDto dto, CancellationToken cancellationToken)
    {
        var path = _pathProvider.GetModFavoritesPath();
        return TomlFileStore.WriteAsync(path, dto, cancellationToken);
    }
}
