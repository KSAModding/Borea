using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// The loader and the loader version that start an instance.
/// </summary>
/// <param name="Choice">The choice of the loader id. It fails also when the loader is only not installed, and then <paramref name="Version"/> says what to install. A choice that succeeds does not mean that the instance can start, because the version that the setting asks for can still be missing, so callers use <see cref="Start"/>.</param>
/// <param name="Listings">The listings the choice was made from.</param>
/// <param name="FromSetting">True when the loader id came from the setting of the instance and not from the caller.</param>
/// <param name="Version">The version that starts or is missing, or null when no loader could be chosen.</param>
public sealed record InstanceLoaderResolution(
    LaunchLoaderChoice Choice,
    IReadOnlyList<ModMetadata> Listings,
    bool FromSetting,
    LoaderVersionResolution? Version)
{
    /// <summary>
    /// The installed version that starts the instance, or null when the instance cannot start. It is null also when the choice
    /// succeeds but the version is not installed, for example a pin while another version of the loader is installed, and then
    /// <see cref="LoaderVersionResolution.Missing"/> or <see cref="LoaderVersionResolution.Problem"/> of <see cref="Version"/> says why.
    /// </summary>
    public InstalledLoaderVersion? Start => Choice.Succeeded ? Version?.Start : null;

    /// <summary>The live listing of the loader, also when it is not installed. Null when no loader could be chosen.</summary>
    public ModMetadata? Loader => Choice.Loader
        ?? (Version is null ? null : Listings.FirstOrDefault(listing => listing.Type == ContentType.ModLoader && ModIds.Equals(listing.ModId, Version.LoaderId)));
}

/// <summary>
/// Chooses the loader and its version for an instance in one place, for the launch, the instance page and the CLI.
/// </summary>
public static class InstanceLoaderResolver
{
    /// <param name="installed">Every installed loader version.</param>
    /// <param name="held">The listings Borea holds without a request, read first as <see cref="LaunchLoaderChoice.ChooseAsync"/> does.</param>
    /// <param name="sources">The live listings, read only when the held ones cannot serve the choice.</param>
    /// <param name="channelMods">The releases to rate and offer, or null when Borea is offline.</param>
    /// <param name="game">The game build of the instance, or null when it is unknown.</param>
    /// <param name="givenLoaderId">The loader the caller named, which wins over the setting of the instance, or null.</param>
    public static async Task<InstanceLoaderResolution> ResolveAsync(
        Instance instance,
        IReadOnlyList<InstalledLoaderVersion> installed,
        IModRepository held,
        IModRepository sources,
        IModRepository? channelMods,
        ReleaseChannel channel,
        GameVersion? game,
        string? givenLoaderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(installed);

        var loaderId = givenLoaderId ?? instance.Loader.LoaderId;
        var fromSetting = givenLoaderId is null && instance.Loader.LoaderId is not null;
        var (choice, listings) = await LaunchLoaderChoice.ChooseAsync(instance, Installations(installed), held, sources, loaderId, cancellationToken).ConfigureAwait(false);

        var chosenId = choice.Failure switch
        {
            LaunchLoaderFailure.None => choice.Loader!.ModId,
            LaunchLoaderFailure.NeededLoaderNotInstalled or LaunchLoaderFailure.GivenLoaderNotInstalled => choice.LoaderIds[0],

            // the install prompt offers the first loader listing by id that can take an instance, so this does the same
            LaunchLoaderFailure.NoLoaderTakesInstance => listings
                .Where(listing => listing.Type == ContentType.ModLoader && listing.Provides?.Instance is not null)
                .OrderBy(listing => listing.ModId, ModIds.Comparer)
                .FirstOrDefault()?.ModId,
            _ => null,
        };
        if (chosenId is null)
            return new InstanceLoaderResolution(choice, listings, fromSetting, Version: null);

        var need = LoaderNeed.For(instance).Loaders.FirstOrDefault(loader => ModIds.Equals(loader.LoaderId, chosenId));

        // a loader that the choice did not find installed has no version to start, so only the missing release is resolved
        var versions = choice.Succeeded ? installed : [];
        var version = await LoaderVersionChoice.ResolveAsync(instance.Loader, chosenId, need, versions, listings, channelMods, channel, game, cancellationToken).ConfigureAwait(false);
        return new InstanceLoaderResolution(choice, listings, fromSetting, version);
    }

    /// <summary>One installation per loader for <see cref="LaunchLoaderChoice"/>: the recorded one, else the newest version.</summary>
    private static Dictionary<string, LoaderInstallation> Installations(IReadOnlyList<InstalledLoaderVersion> installed)
    {
        var installations = new Dictionary<string, LoaderInstallation>(ModIds.Comparer);
        foreach (var group in installed.GroupBy(version => version.LoaderId, ModIds.Comparer))
        {
            var version = group.FirstOrDefault(entry => entry.IsRecorded)
                ?? group.OrderByDescending(entry => entry.Version is not null).ThenByDescending(entry => entry.Version).First();
            installations[group.Key] = new LoaderInstallation(version.DirectoryPath, version.Version, version.RawVersion, version.IsAdopted);
        }

        return installations;
    }
}
