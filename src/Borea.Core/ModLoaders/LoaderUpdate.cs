using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// How the installed release of a mod loader fits the installed game, and which
/// release Borea offers in its place.
/// </summary>
/// <param name="InstalledVersion">The installed release, or null when it is not installed or its version is unknown.</param>
/// <param name="InstalledCompatibility">What the index says about the installed release and the installed game.</param>
/// <param name="Offer">
/// The newest offered release of the channel that the index does not rate incompatible
/// with the game, and only when it is above the installed version. Null when there is none.
/// </param>
/// <param name="InstalledNeedsNewerGame">True when the installed release starts after the installed game, so a KSA update fixes it.</param>
public sealed record LoaderUpdate(string LoaderId, ModVersion? InstalledVersion, GameCompatibility InstalledCompatibility, ModVersionMetadata? Offer, bool InstalledNeedsNewerGame = false)
{
    /// <summary>True when the installed release does not support the game and a newer release does.</summary>
    public bool IsUrgent => InstalledCompatibility == GameCompatibility.Incompatible && Offer is not null;

    /// <param name="installation">The recorded loader, or null when it is not installed.</param>
    /// <param name="game">The installed game. Null rates nothing and offers the newest release of the channel.</param>
    /// <param name="need">
    /// What the mods of an instance accept. A release outside that range is never offered, and a range that
    /// is empty limits nothing. Null offers without that limit.
    /// </param>
    public static async Task<LoaderUpdate> FindAsync(
        IModRepository mods,
        ReleaseChannel channel,
        string loaderId,
        LoaderInstallation? installation,
        GameVersion? game,
        CancellationToken cancellationToken = default,
        NeededLoader? need = null)
    {
        ArgumentNullException.ThrowIfNull(mods);

        // the channel repository does not filter its versions, so the channel is applied here
        while (mods is ReleaseChannelModRepository channelled)
            mods = channelled.Inner;

        var installedVersion = installation?.Version;
        var compatibility = GameCompatibility.Unknown;
        var needsNewerGame = false;
        if (installedVersion is { } version && await mods.GetReleaseAsync(loaderId, version, cancellationToken).ConfigureAwait(false) is { } installed)
        {
            compatibility = Compatibility.Evaluate(installed, game);
            needsNewerGame = compatibility == GameCompatibility.Incompatible && !Compatibility.IsAboveUpperBound(installed, game);
        }

        ModVersionMetadata? offer = null;
        foreach (var candidate in (await mods.GetAvailableVersionsAsync(loaderId, cancellationToken).ConfigureAwait(false)).OrderByDescending(value => value))
        {
            // no downgrade: a release at or below the installed one is never offered
            if (installedVersion is { } current && candidate.CompareTo(current) <= 0)
                break;

            var release = await mods.GetReleaseAsync(loaderId, candidate, cancellationToken).ConfigureAwait(false);
            if (release is { IsOffered: true }
                && channel.Includes(release.ReleaseStatus)
                && (need is null || need.HasConflict || need.Accepts(candidate))
                && Compatibility.Evaluate(release, game) != GameCompatibility.Incompatible)
            {
                offer = release;
                break;
            }
        }

        return new LoaderUpdate(loaderId, installedVersion, compatibility, offer, needsNewerGame);
    }
}
