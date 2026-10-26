using System.Diagnostics.CodeAnalysis;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// The installed mod loader that starts an instance.
/// </summary>
public sealed class LaunchLoaderChoice
{
    /// <summary>The live listing of the chosen loader. Null when the choice failed.</summary>
    public ModMetadata? Loader { get; }

    /// <summary>Whether a mod of the instance needs the chosen loader.</summary>
    public bool RequiredByMods { get; }

    public LaunchLoaderFailure Failure { get; }

    /// <summary>The loader ids the failure names, ordered by id. Empty on success.</summary>
    public IReadOnlyList<string> LoaderIds { get; }

    /// <summary>
    /// Whether the listings lacked a mod loader listing that the choice needs,
    /// so that newer listings can give another choice. False on success.
    /// </summary>
    public bool LacksListing { get; }

    [MemberNotNullWhen(true, nameof(Loader))]
    public bool Succeeded => Failure == LaunchLoaderFailure.None;

    private LaunchLoaderChoice(ModMetadata? loader, bool requiredByMods, LaunchLoaderFailure failure, IReadOnlyList<string> loaderIds, bool lacksListing)
    {
        Loader = loader;
        RequiredByMods = requiredByMods;
        Failure = failure;
        LoaderIds = loaderIds;
        LacksListing = lacksListing;
    }

    /// <param name="listings">The live listings. Only mod loader listings count.</param>
    /// <param name="loaderId">The loader the user named, or null.</param>
    public static LaunchLoaderChoice Choose(
        Instance instance,
        IReadOnlyDictionary<string, LoaderInstallation> installations,
        IReadOnlyList<ModMetadata> listings,
        string? loaderId = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentNullException.ThrowIfNull(listings);

        var needed = instance.Mods
            .Select(mod => mod.Metadata.Loader?.LoaderId)
            .OfType<string>()
            .Distinct(ModIds.Comparer)
            .Order(ModIds.Comparer)
            .ToList();

        if (loaderId is not null)
            return Installed(loaderId, needed.Contains(loaderId, ModIds.Comparer), LaunchLoaderFailure.GivenLoaderNotInstalled);

        if (needed.Count > 1)
            return Failed(LaunchLoaderFailure.DifferentLoadersNeeded, needed, lacksListing: false);

        if (needed.Count == 1)
            return Installed(needed[0], requiredByMods: true, LaunchLoaderFailure.NeededLoaderNotInstalled);

        var recorded = installations.Keys.Order(ModIds.Comparer).ToList();
        var takesInstance = recorded
            .Select(Listing)
            .FirstOrDefault(listing => listing?.Provides?.Instance is not null);
        if (takesInstance is not null)
            return new LaunchLoaderChoice(takesInstance, requiredByMods: false, LaunchLoaderFailure.None, [], lacksListing: false);

        var notListed = recorded.Where(id => Listing(id) is null).ToList();
        if (notListed.Count > 0)
            return Failed(LaunchLoaderFailure.LoaderNotListed, notListed, lacksListing: true);

        var hasListingThatTakesInstance = listings.Any(listing => listing.Type == ContentType.ModLoader && listing.Provides?.Instance is not null);
        return Failed(LaunchLoaderFailure.NoLoaderTakesInstance, [], lacksListing: !hasListingThatTakesInstance);

        LaunchLoaderChoice Installed(string id, bool requiredByMods, LaunchLoaderFailure notInstalled)
        {
            if (!installations.Keys.Any(key => ModIds.Equals(key, id)))
                return Failed(notInstalled, [id], lacksListing: Listing(id) is null);

            return Listing(id) is { } listing
                ? new LaunchLoaderChoice(listing, requiredByMods, LaunchLoaderFailure.None, [], lacksListing: false)
                : Failed(LaunchLoaderFailure.LoaderNotListed, [id], lacksListing: true);
        }

        ModMetadata? Listing(string id) =>
            listings.FirstOrDefault(listing => listing.Type == ContentType.ModLoader && ModIds.Equals(listing.ModId, id));
    }

    /// <summary>
    /// Chooses from the listings that <paramref name="held"/> serves without a request.
    /// Only when it has none, or they lack a mod loader listing that the choice needs,
    /// does it read <paramref name="sources"/>.
    /// </summary>
    /// <returns>The choice, and the listings it was made from.</returns>
    public static async Task<(LaunchLoaderChoice Choice, IReadOnlyList<ModMetadata> Listings)> ChooseAsync(
        Instance instance,
        IReadOnlyDictionary<string, LoaderInstallation> installations,
        IModRepository held,
        IModRepository sources,
        string? loaderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(sources);

        IReadOnlyList<ModMetadata>? heldListings;
        try
        {
            heldListings = await held.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Borea holds no listings yet, or none it can read
            heldListings = null;
        }

        if (heldListings is not null && Choose(instance, installations, heldListings, loaderId) is { LacksListing: false } choice)
            return (choice, heldListings);

        var listings = await sources.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
        return (Choose(instance, installations, listings, loaderId), listings);
    }

    private static LaunchLoaderChoice Failed(LaunchLoaderFailure failure, IReadOnlyList<string> loaderIds, bool lacksListing) =>
        new(loader: null, requiredByMods: false, failure, loaderIds, lacksListing);
}
