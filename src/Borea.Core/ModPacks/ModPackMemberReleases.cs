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
    /// The members that have a newer release, in pack order. A newer release counts when
    /// <paramref name="channel"/> or the narrowest channel of the pinned release offers it,
    /// so a stable pin is outdated only by a newer stable release unless the player chose a
    /// wider channel. A pin that <paramref name="mods"/> does not list is left out.
    /// </summary>
    public static async Task<IReadOnlyList<NewerMemberRelease>> FindAsync(ModPackMetadata pack, IModRepository mods, ReleaseChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(mods);

        var newer = new List<NewerMemberRelease>();
        foreach (var pin in pack.Mods)
        {
            var pinned = await mods.GetReleaseAsync(pin.ContentId, pin.Version, cancellationToken).ConfigureAwait(false);
            if (pinned is null)
                continue;

            var pinnedChannel = ReleaseChannels.NarrowestFor(pinned.ReleaseStatus);
            var latest = await mods.GetLatestReleaseInChannelAsync(pin.ContentId, channel > pinnedChannel ? channel : pinnedChannel, cancellationToken).ConfigureAwait(false);
            if (latest is not null && latest.Version > pin.Version)
                newer.Add(new NewerMemberRelease(pin.ContentId, pin.Version, latest));
        }

        return newer;
    }
}
