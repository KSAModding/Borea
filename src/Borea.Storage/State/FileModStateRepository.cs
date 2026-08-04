using Borea.Core.Paths;
using Borea.Core.State;
using Borea.Storage.Toml;

namespace Borea.Storage.State;

/// <summary>
/// File-backed implementation of IModStateRepository, reading and writing
/// the instance's manifest.toml directly.
/// </summary>
public sealed class FileModStateRepository : IModStateRepository
{
    private readonly IGamePathProvider _pathProvider;

    public FileModStateRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);
        return manifest.Mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.Ordinal))?.Enabled ?? false;
    }

    public async Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);
        return manifest.Mods.Where(m => m.Enabled).Select(m => m.Id).ToList();
    }

    public async Task SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);
        var entry = manifest.Mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.Ordinal));

        if (entry is null)
            manifest.Mods.Add(new ModManifestEntryDto { Id = modId, Enabled = true });
        else if (!entry.Enabled)
            entry.Enabled = true;
        else
            return;

        await WriteManifestAsync(instanceId, manifest, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(instanceId, cancellationToken).ConfigureAwait(false);
        var entry = manifest.Mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.Ordinal));

        if (entry is null || !entry.Enabled)
            return;

        entry.Enabled = false;
        await WriteManifestAsync(instanceId, manifest, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManifestDto> ReadManifestAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var path = _pathProvider.GetInstanceManifestPath(instanceId);
        var manifest = await TomlFileStore.ReadAsync<ManifestDto>(path, cancellationToken).ConfigureAwait(false);
        return manifest ?? new ManifestDto();
    }

    private Task WriteManifestAsync(Guid instanceId, ManifestDto manifest, CancellationToken cancellationToken)
    {
        var path = _pathProvider.GetInstanceManifestPath(instanceId);
        return TomlFileStore.WriteAsync(path, manifest, cancellationToken);
    }
}
