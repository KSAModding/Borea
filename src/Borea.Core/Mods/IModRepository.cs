namespace Borea.Core.Mods;

/// <summary>
/// Read access to the content available from one of Borea's sources. Listing
/// methods return the live <see cref="ModMetadata"/>; release methods return
/// the stamped <see cref="ModVersionMetadata"/> of one concrete version.
/// Installed mods keep their own snapshot via <see cref="InstalledMod.Metadata"/>.
/// </summary>
public interface IModRepository
{
    /// <summary>
    /// Retrieves the listings of all mods currently available from the source.
    /// </summary>
    Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the newest available release of a mod, skipping yanked
    /// releases, or null if the mod has no usable release.
    /// </summary>
    Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves one specific release of a mod, or null if that version is not
    /// available. A yanked release is returned as-is, because an already
    /// installed copy may still need its data.
    /// </summary>
    Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all versions with a usable release for a given mod, ordered
    /// newest-first. Empty if the mod is not currently available.
    /// </summary>
    Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches available mod listings by name/tag/description text.
    /// </summary>
    Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
