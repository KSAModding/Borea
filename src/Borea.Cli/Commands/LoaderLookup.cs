using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

internal static class LoaderLookup
{
    /// <summary>
    /// The game build that the loader of <paramref name="instance"/> starts, and its folder. Every instance uses the
    /// game in the settings today, and per-instance game builds replace the body of this method.
    /// </summary>
    public static (GameVersion? Version, string? Directory) LoaderGameOf(CliServices cli, Instance instance)
    {
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(instance);
        return (cli.InstalledVersion.GetInstalledVersion()?.Version, cli.Paths.GetGameDirectoryPath());
    }

    /// <summary>
    /// The loader and the loader version that start <paramref name="instance"/>, from the same inputs for the launch and
    /// for <c>instance show</c>: the held listings before the live ones, and the held releases to rate and offer.
    /// </summary>
    public static async Task<InstanceLoaderResolution> ResolveAsync(
        CliServices cli,
        Instance instance,
        IReadOnlyList<InstalledLoaderVersion> installed,
        GameVersion? game,
        string? givenLoaderId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cli);
        var releases = await HeldReleasesAsync(cli.OfflineContentIndex, cancellationToken).ConfigureAwait(false);
        return await InstanceLoaderResolver
            .ResolveAsync(instance, installed, cli.OfflineContentIndex, cli.Mods, releases, cli.Settings.ReleaseChannel, game, givenLoaderId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The releases that Borea holds without a request, or null when it holds no index it can read. The launch rates
    /// and offers loader versions from them, so it never asks the network for a release.
    /// </summary>
    public static async Task<IModRepository?> HeldReleasesAsync(IModRepository held, CancellationToken cancellationToken)
    {
        try
        {
            await held.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
            return held;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public static async Task<ModMetadata> GetListingAsync(
        IModRepository repository,
        string loaderId,
        CancellationToken cancellationToken)
    {
        var listings = await repository.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
        return GetListing(listings, loaderId);
    }

    public static ModMetadata GetListing(IReadOnlyList<ModMetadata> listings, string loaderId)
    {
        var listing = listings.FirstOrDefault(candidate => ModIds.Equals(candidate.ModId, loaderId));
        if (listing is null)
            throw new InvalidOperationException($"Mod loader '{loaderId}' is not available from the configured sources.");

        if (listing.Type != ContentType.ModLoader)
            throw new InvalidOperationException($"'{listing.ModId}' is a {listing.Type}, not a mod loader.");

        return listing;
    }
}
