using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

/// <summary>Provides read access to mod packs from Borea's content index.</summary>
public interface IModPackRepository
{
    /// <summary>Lists packs with a usable, non-retracted latest version, ordered by id.</summary>
    Task<IReadOnlyList<ModPackResult>> GetAvailableModPacksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a pack identity and its latest usable metadata.
    /// A known delisted or unsupported pack returns an identity-only result.
    /// </summary>
    Task<ModPackResult?> GetAsync(string modPackId, CancellationToken cancellationToken = default);

    /// <summary>Gets the newest usable version, excluding retracted and unsupported versions.</summary>
    Task<ModPackResult?> GetLatestAsync(string modPackId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one exact version, including a retracted version and its warning.
    /// An unsupported version returns an identity-only result with its diagnostic.
    /// </summary>
    Task<ModPackResult?> GetVersionAsync(string modPackId, ModVersion version, CancellationToken cancellationToken = default);

    /// <summary>Lists usable versions newest-first, excluding retracted and unsupported versions.</summary>
    Task<IReadOnlyList<ModPackResult>> GetAvailableVersionsAsync(string modPackId, CancellationToken cancellationToken = default);

    /// <summary>Searches packs with usable latest metadata and orders matches by id.</summary>
    Task<IReadOnlyList<ModPackResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
