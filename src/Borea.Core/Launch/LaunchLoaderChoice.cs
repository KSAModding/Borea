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

    [MemberNotNullWhen(true, nameof(Loader))]
    public bool Succeeded => Failure == LaunchLoaderFailure.None;

    private LaunchLoaderChoice(ModMetadata? loader, bool requiredByMods, LaunchLoaderFailure failure, IReadOnlyList<string> loaderIds)
    {
        Loader = loader;
        RequiredByMods = requiredByMods;
        Failure = failure;
        LoaderIds = loaderIds;
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
            return Failed(LaunchLoaderFailure.DifferentLoadersNeeded, needed);

        if (needed.Count == 1)
            return Installed(needed[0], requiredByMods: true, LaunchLoaderFailure.NeededLoaderNotInstalled);

        var recorded = installations.Keys.Order(ModIds.Comparer).ToList();
        var takesInstance = recorded
            .Select(Listing)
            .FirstOrDefault(listing => listing?.Provides?.Instance is not null);
        if (takesInstance is not null)
            return new LaunchLoaderChoice(takesInstance, requiredByMods: false, LaunchLoaderFailure.None, []);

        var notListed = recorded.Where(id => Listing(id) is null).ToList();
        return notListed.Count > 0
            ? Failed(LaunchLoaderFailure.LoaderNotListed, notListed)
            : Failed(LaunchLoaderFailure.NoLoaderTakesInstance, []);

        LaunchLoaderChoice Installed(string id, bool requiredByMods, LaunchLoaderFailure notInstalled)
        {
            if (!installations.Keys.Any(key => ModIds.Equals(key, id)))
                return Failed(notInstalled, [id]);

            return Listing(id) is { } listing
                ? new LaunchLoaderChoice(listing, requiredByMods, LaunchLoaderFailure.None, [])
                : Failed(LaunchLoaderFailure.LoaderNotListed, [id]);
        }

        ModMetadata? Listing(string id) =>
            listings.FirstOrDefault(listing => listing.Type == ContentType.ModLoader && ModIds.Equals(listing.ModId, id));
    }

    private static LaunchLoaderChoice Failed(LaunchLoaderFailure failure, IReadOnlyList<string> loaderIds) =>
        new(loader: null, requiredByMods: false, failure, loaderIds);
}
