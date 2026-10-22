using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>Why the version that starts an instance is not a plain fit, or why no version starts it.</summary>
public enum LoaderVersionProblem
{
    /// <summary>A version starts, and nothing says it does not fit.</summary>
    None = 0,

    /// <summary>The index rates the version that starts incompatible with the game.</summary>
    GameIncompatible = 1,

    /// <summary>The version that starts is outside the range that the mods of the instance accept.</summary>
    OutsideModRange = 2,

    /// <summary>No version starts because the version is not installed. <see cref="LoaderVersionResolution.Missing"/> names the release to install when the index has it.</summary>
    NotInstalled = 3,

    /// <summary>The pinned version is not installed, and the index does not offer it because it is yanked, its download is gone, or it is not listed.</summary>
    NotInstallable = 4,

    /// <summary>No version is installed, and the index offers no release that fits the game and the mods.</summary>
    NoRelease = 5,
}

/// <summary>
/// Which installed version of one loader starts an instance, and what Borea offers to install.
/// </summary>
/// <param name="Start">The installed version that starts the instance, or null when none does.</param>
/// <param name="IsPinned">True when the setting of the instance names an exact version.</param>
/// <param name="Missing">The release to install before the instance can start, or null.</param>
/// <param name="Offer">A newer release that fits the game and the mods and is not installed. A pinned instance never gets one.</param>
/// <param name="StartCompatibility">What the index says about <paramref name="Start"/> and the game.</param>
public sealed record LoaderVersionResolution(
    string LoaderId,
    InstalledLoaderVersion? Start,
    bool IsPinned,
    ModVersionMetadata? Missing,
    ModVersionMetadata? Offer,
    GameCompatibility StartCompatibility,
    LoaderVersionProblem Problem)
{
    /// <summary>True when the version that starts does not support the game and a newer release does, the urgent case of the update banner.</summary>
    public bool IsUrgent => StartCompatibility == GameCompatibility.Incompatible && Offer is not null;
}

/// <summary>
/// The one rule that chooses the loader version of an instance. It reads no files, so the launch, the instance page,
/// the CLI and the check for unused versions all get the same answer from the same inputs.
/// </summary>
public static class LoaderVersionChoice
{
    /// <param name="setting">The loader setting of the instance. A pin applies only when it names <paramref name="loaderId"/>.</param>
    /// <param name="need">What the mods of the instance accept of this loader, or null when no mod needs it.</param>
    /// <param name="installed">The installed versions. Versions of other loaders are ignored.</param>
    /// <param name="listings">The listings the loader was chosen from. Releases are read only when they list the loader.</param>
    /// <param name="mods">The releases of the index, or null when Borea is offline. Then every rating is unknown and nothing is offered.</param>
    /// <param name="game">The game build of the instance, or null when it is unknown.</param>
    public static async Task<LoaderVersionResolution> ResolveAsync(
        LoaderSetting setting,
        string loaderId,
        NeededLoader? need,
        IReadOnlyList<InstalledLoaderVersion> installed,
        IReadOnlyList<ModMetadata> listings,
        IModRepository? mods,
        ReleaseChannel channel,
        GameVersion? game,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(listings);
        ModIds.Validate(loaderId, nameof(loaderId));

        var index = mods is not null && listings.Any(listing => listing.Type == ContentType.ModLoader && ModIds.Equals(listing.ModId, loaderId))
            ? mods
            : null;
        var versions = InstalledLoaderVersion.Of(installed, loaderId);

        if (setting.Version is { } pin && ModIds.Equals(setting.LoaderId, loaderId))
            return await PinnedAsync(pin).ConfigureAwait(false);

        var inChannel = new List<InstalledLoaderVersion>();
        foreach (var version in versions)
        {
            if (await IsInChannelAsync(version).ConfigureAwait(false))
                inChannel.Add(version);
        }

        // A version outside the channel serves only the instances that pin it. The recorded one still starts when nothing
        // else is installed, because an older Borea and the single install of today start it in every channel.
        if (inChannel.Count == 0 && versions.FirstOrDefault(version => version.IsRecorded) is { } recorded)
            inChannel.Add(recorded);

        if (inChannel.Count == 0)
        {
            var missing = index is null ? null : (await FindAsync(installation: null).ConfigureAwait(false)).Offer;
            var problem = missing is not null || index is null ? LoaderVersionProblem.NotInstalled : LoaderVersionProblem.NoRelease;
            return new LoaderVersionResolution(loaderId, null, IsPinned: false, missing, Offer: null, GameCompatibility.Unknown, problem);
        }

        var rated = new List<(InstalledLoaderVersion Version, GameCompatibility Compatibility)>();
        foreach (var version in Newest(inChannel))
            rated.Add((version, await RateAsync(version).ConfigureAwait(false)));

        var start = rated.FirstOrDefault(entry => entry.Compatibility != GameCompatibility.Incompatible && Accepts(entry.Version));
        start = start.Version is not null ? start : rated.FirstOrDefault(entry => entry.Compatibility != GameCompatibility.Incompatible);
        start = start.Version is not null ? start : rated[0];

        ModVersionMetadata? offer = null;
        if (index is not null)
        {
            var update = await FindAsync(new LoaderInstallation(start.Version.DirectoryPath, start.Version.Version, start.Version.RawVersion, start.Version.IsAdopted)).ConfigureAwait(false);
            if (update.Offer is { } release && InstalledLoaderVersion.Find(versions, loaderId, release.Version) is null)
                offer = release;
        }

        return new LoaderVersionResolution(loaderId, start.Version, IsPinned: false, Missing: null, offer, start.Compatibility, ProblemOf(start.Version, start.Compatibility));

        async Task<LoaderVersionResolution> PinnedAsync(ModVersion pin)
        {
            if (InstalledLoaderVersion.Find(versions, loaderId, pin) is { } pinned)
            {
                var compatibility = await RateAsync(pinned).ConfigureAwait(false);
                return new LoaderVersionResolution(loaderId, pinned, IsPinned: true, Missing: null, Offer: null, compatibility, ProblemOf(pinned, compatibility));
            }

            // An exact version installs from any channel, but Borea never installs a yanked or gone release.
            if (index is null)
                return new LoaderVersionResolution(loaderId, null, IsPinned: true, Missing: null, Offer: null, GameCompatibility.Unknown, mods is null ? LoaderVersionProblem.NotInstalled : LoaderVersionProblem.NotInstallable);

            var release = await index.GetReleaseAsync(loaderId, pin, cancellationToken).ConfigureAwait(false);
            return release is { IsOffered: true }
                ? new LoaderVersionResolution(loaderId, null, IsPinned: true, release, Offer: null, GameCompatibility.Unknown, LoaderVersionProblem.NotInstalled)
                : new LoaderVersionResolution(loaderId, null, IsPinned: true, Missing: null, Offer: null, GameCompatibility.Unknown, LoaderVersionProblem.NotInstallable);
        }

        async Task<bool> IsInChannelAsync(InstalledLoaderVersion version)
        {
            if (version.Version is not { } known)
                return true;

            if (index is not null && await index.GetReleaseAsync(loaderId, known, cancellationToken).ConfigureAwait(false) is { } release)
                return channel.Includes(release.ReleaseStatus);

            return known.PreRelease is null || channel != ReleaseChannel.Stable;
        }

        async Task<GameCompatibility> RateAsync(InstalledLoaderVersion version)
        {
            if (index is null || version.Version is not { } known)
                return GameCompatibility.Unknown;

            return await index.GetReleaseAsync(loaderId, known, cancellationToken).ConfigureAwait(false) is { } release
                ? Compatibility.Evaluate(release, game)
                : GameCompatibility.Unknown;
        }

        // A version Borea does not know cannot be checked against the range, so it counts as accepted, as it starts today.
        bool Accepts(InstalledLoaderVersion version) =>
            need is null || need.HasConflict || version.Version is not { } known || need.Accepts(known);

        LoaderVersionProblem ProblemOf(InstalledLoaderVersion version, GameCompatibility compatibility) =>
            compatibility == GameCompatibility.Incompatible ? LoaderVersionProblem.GameIncompatible
            : Accepts(version) ? LoaderVersionProblem.None
            : LoaderVersionProblem.OutsideModRange;

        Task<LoaderUpdate> FindAsync(LoaderInstallation? installation) =>
            LoaderUpdate.FindAsync(index!, channel, loaderId, installation, game, cancellationToken, need);
    }

    private static IEnumerable<InstalledLoaderVersion> Newest(IEnumerable<InstalledLoaderVersion> versions) =>
        versions.OrderByDescending(version => version.Version is not null).ThenByDescending(version => version.Version);
}
