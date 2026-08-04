namespace Borea.Core.Mods;

/// <summary>
/// Provides read access to mods available from Borea's mod source (e.g. the
/// KSA mod registry). Only reflects currently-available, non-removed mods
/// and versions — installed mods retain their own metadata snapshot via
/// <see cref="InstalledMod.Metadata"/> independent of this interface.
/// </summary>
public interface IModRepository
{
    /// <summary>
    /// Retrieves all mods currently available from the source.
    /// </summary>
    Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata for a specific mod at its latest available version,
    /// or null if the mod is not currently available (removed/unlisted/unknown).
    /// </summary>
    Task<ModMetadata?> GetLatestAsync(string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata for a specific mod at a specific version, or null
    /// if that mod/version is not currently available.
    /// </summary>
    Task<ModMetadata?> GetVersionAsync(string modId, ModVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all versions currently available for a given mod, ordered
    /// newest-first. Empty if the mod is not currently available.
    /// </summary>
    Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches available mods by name/tag/description text.
    /// </summary>
    Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
