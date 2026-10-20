using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

/// <summary>A pack member whose mod, vehicle or save has a newer release than the version the pack pins.</summary>
public sealed record NewerMemberRelease(string ModId, ModVersion Pinned, ModVersionMetadata Newer, ContentType Type = ContentType.Mod);

/// <summary>
/// Which members of a pack have newer releases. A pack pins exact versions on purpose,
/// so this only marks the pack and never changes what it installs.
/// </summary>
public static class ModPackMemberReleases
{
    /// <summary>
    /// The members that have a newer release by <see cref="NewerRelease"/>, in pack order. It reads the snapshot only,
    /// so every player sees the same members whatever their channel. A pin on an unlisted mod is left out.
    /// </summary>
    public static IReadOnlyList<NewerMemberRelease> Find(ModPackMetadata pack, ContentIndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(snapshot);

        var newer = new List<NewerMemberRelease>();
        foreach (var (type, pins) in new[] { (ContentType.Mod, pack.Mods), (ContentType.Vehicle, pack.Vehicles), (ContentType.Save, pack.Saves) })
        {
            foreach (var pin in pins)
            {
                if (NewerRelease(snapshot, pin.ContentId, pin.Version, type) is { } release)
                    newer.Add(new NewerMemberRelease(pin.ContentId, pin.Version, release, type));
            }
        }

        return newer;
    }

    /// <summary>
    /// The newest release that makes a pin outdated by the "Newer releases" rule of RFC 0080: higher precedence, not yanked,
    /// still downloadable (RFC 0078), and at least as stable as the pinned release. It reads the snapshot only, so it does not depend on the player's channel.
    /// A pin on a release that the snapshot does not have is held to stable, the strictest level.
    /// </summary>
    /// <param name="type">The type of the pack section, which says in which list of the snapshot the id is.</param>
    public static ModVersionMetadata? NewerRelease(ContentIndexSnapshot snapshot, string modId, ModVersion pinned, ContentType type = ContentType.Mod)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var listings = type switch
        {
            ContentType.Vehicle => snapshot.Vehicles,
            ContentType.Save => snapshot.Saves,
            _ => snapshot.Listings,
        };
        if (listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, modId)) is not { } listing)
            return null;

        var status = listing.Releases.FirstOrDefault(release => release.Version == pinned)?.ReleaseStatus ?? ReleaseStatus.Stable;
        var channel = ReleaseChannels.NarrowestFor(status);
        return listing.Releases
            .Where(release => release.Version > pinned && release.IsOffered && channel.Includes(release.ReleaseStatus))
            .MaxBy(release => release.Version);
    }
}
