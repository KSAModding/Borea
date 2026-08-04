using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Toml;

namespace Borea.Storage.Settings;

public sealed class FileBoreaSettingsRepository : IBoreaSettingsRepository
{
    private readonly IGamePathProvider _pathProvider;

    public FileBoreaSettingsRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<BoreaSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        var path = _pathProvider.GetBoreaSettingsPath();
        var dto = await TomlFileStore.ReadAsync<BoreaSettingsDto>(path, cancellationToken).ConfigureAwait(false);
        return dto is null ? null : BoreaSettingsMapper.FromDto(dto);
    }

    public Task SaveAsync(BoreaSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        var path = _pathProvider.GetBoreaSettingsPath();
        return TomlFileStore.WriteAsync(path, BoreaSettingsMapper.ToDto(settings), cancellationToken);
    }
}
