using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

/// <summary>A pack member whose mod has a newer release than the version the pack pins.</summary>
public sealed record NewerMemberRelease(string ModId, ModVersion Pinned, ModVersionMetadata Newer);

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
        foreach (var pin in pack.Mods)
        {
            if (NewerRelease(snapshot, pin.ContentId, pin.Version) is { } release)
                newer.Add(new NewerMemberRelease(pin.ContentId, pin.Version, release));
        }

        return newer;
    }

    /// <summary>
    /// The newest release that makes a pin outdated by the "Newer releases" rule of RFC 0080: higher precedence, not yanked,
    /// still downloadable (RFC 0078), and at least as stable as the pinned release. It reads the snapshot only, so it does not depend on the player's channel.
    /// A pin on a release that the snapshot does not have is held to stable, the strictest level.
    /// </summary>
    public static ModVersionMetadata? NewerRelease(ContentIndexSnapshot snapshot, string modId, ModVersion pinned)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, modId)) is not { } listing)
            return null;

        var status = listing.Releases.FirstOrDefault(release => release.Version == pinned)?.ReleaseStatus ?? ReleaseStatus.Stable;
        var channel = ReleaseChannels.NarrowestFor(status);
        return listing.Releases
            .Where(release => release.Version > pinned && release.IsOffered && channel.Includes(release.ReleaseStatus))
            .MaxBy(release => release.Version);
    }
}
