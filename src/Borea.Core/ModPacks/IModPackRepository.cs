using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

/// <summary>
/// Provides read access to mod packs available from Borea's pack source.
/// Only reflects currently-available, non-removed packs and versions.
/// </summary>
public interface IModPackRepository
{
    Task<IReadOnlyList<ModPackMetadata>> GetAvailableModPacksAsync(CancellationToken cancellationToken = default);

    Task<ModPackMetadata?> GetLatestAsync(string modPackId, CancellationToken cancellationToken = default);

    Task<ModPackMetadata?> GetVersionAsync(string modPackId, ModVersion version, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modPackId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModPackMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default);
}