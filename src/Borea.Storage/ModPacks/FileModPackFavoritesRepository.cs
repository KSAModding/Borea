using Borea.Core.ModPacks;
using Borea.Core.Paths;
using Borea.Storage.Toml;

namespace Borea.Storage.ModPacks;

public sealed class FileModPackFavoritesRepository : IModPackFavoritesRepository
{
    private readonly IGamePathProvider _pathProvider;

    public FileModPackFavoritesRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<IReadOnlyList<string>> GetFavoriteModPackIdsAsync(CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return dto.ModPackIds;
    }

    public async Task<bool> IsFavoriteAsync(string modPackId, CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return dto.ModPackIds.Contains(modPackId, StringComparer.Ordinal);
    }

    public async Task AddFavoriteAsync(string modPackId, CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync(cancellationToken).ConfigureAwait(false);

        if (dto.ModPackIds.Contains(modPackId, StringComparer.Ordinal))
            return;

        dto.ModPackIds.Add(modPackId);
        await WriteAsync(dto, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveFavoriteAsync(string modPackId, CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync(cancellationToken).ConfigureAwait(false);

        if (dto.ModPackIds.RemoveAll(id => string.Equals(id, modPackId, StringComparison.Ordinal)) > 0)
            await WriteAsync(dto, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModPackFavoritesDto> ReadAsync(CancellationToken cancellationToken)
    {
        var path = _pathProvider.GetModPackFavoritesPath();
        var dto = await TomlFileStore.ReadAsync<ModPackFavoritesDto>(path, cancellationToken).ConfigureAwait(false);
        return dto ?? new ModPackFavoritesDto();
    }

    private Task WriteAsync(ModPackFavoritesDto dto, CancellationToken cancellationToken)
    {
        var path = _pathProvider.GetModPackFavoritesPath();
        return TomlFileStore.WriteAsync(path, dto, cancellationToken);
    }
}