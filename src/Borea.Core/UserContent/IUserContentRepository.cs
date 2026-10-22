using Borea.Core.Mods;

namespace Borea.Core.UserContent;

/// <summary>
/// The listed vehicles and saves (RFC 0085). It is kept apart from <see cref="IModRepository"/>,
/// so the planner never takes a vehicle or a save as a dependency: a dependency names a mod.
/// A delisted listing is left out, as it is for mods.
/// </summary>
public interface IUserContentRepository
{
    /// <summary>The listing of a vehicle or save, or null when the id names none.</summary>
    Task<ModMetadata?> FindAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>One release, also a yanked one or one whose download is gone, as <see cref="IModRepository.GetReleaseAsync"/> returns it.</summary>
    Task<ModVersionMetadata?> GetReleaseAsync(string id, ModVersion version, CancellationToken cancellationToken = default);

    /// <summary>The newest release that Borea may pick by itself and that the channel offers, or null.</summary>
    Task<ModVersionMetadata?> GetLatestReleaseAsync(string id, ReleaseChannel channel, CancellationToken cancellationToken = default);

    /// <summary>Every listed vehicle and save.</summary>
    Task<IReadOnlyList<ModMetadata>> GetAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>The releases a player can pick by hand, newest first: one per version and none that is yanked, as <see cref="IModRepository.GetReleaseHistoryAsync"/>.</summary>
    Task<IReadOnlyList<ModVersionMetadata>> GetReleaseHistoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>The listings of one type that match the query, as a mod search matches.</summary>
    Task<IReadOnlyList<ModMetadata>> SearchAsync(ContentType type, string query, CancellationToken cancellationToken = default);
}
