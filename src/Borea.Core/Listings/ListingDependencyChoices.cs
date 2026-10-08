using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Listings;

/// <summary>A dependency that a mod.toml declares, as the stamper derives it: required, or optional when the block says Optional.</summary>
public sealed record ListingDeclaredDependency(string Id, string Kind);

/// <param name="Release">The release whose mod.toml was read, as its version or its tag.</param>
public sealed record ListingDeclaredDependencies(string Release, IReadOnlyList<ListingDeclaredDependency> Dependencies);

/// <summary>
/// What the dependency entries of a listing can name: the listed mods and loaders, their stamped releases, and the
/// dependencies the mod.toml of a release declares, which the index derives by itself.
/// </summary>
public static class ListingDependencyChoices
{
    public const string Required = "required";

    public const string Optional = "optional";

    /// <summary>The listings an entry can name and a client can install, in the order of the snapshot.</summary>
    public static IReadOnlyList<ContentIndexListing> Candidates(ContentIndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Listings.Where(IsCandidate).ToList();
    }

    /// <summary>The listed mod or loader of an id, or null when the snapshot lists none a client can install.</summary>
    public static ContentIndexListing? Listing(ContentIndexSnapshot snapshot, string id)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, id) && IsCandidate(listing));
    }

    /// <summary>Whether a listing or a pack of any type and state holds the id, as the checks of the index see it.</summary>
    public static bool IsHeld(ContentIndexSnapshot snapshot, string id)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Listings.Any(listing => ModIds.Equals(listing.Id, id)) || snapshot.Packs.Any(pack => ModIds.Equals(pack.Id, id));
    }

    /// <summary>A loader goes into [loader], because the index refuses a dependency on it and keeps its bounds there.</summary>
    public static bool IsLoader(ContentIndexListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return listing.Authored?.Type == ContentType.ModLoader;
    }

    /// <summary>The stamped releases a bound can name, newest first, without the yanked ones.</summary>
    public static IReadOnlyList<ModVersionMetadata> Releases(ContentIndexListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return listing.Releases.Where(release => !release.Yanked).OrderByDescending(release => release.Version).ToList();
    }

    /// <summary>The release "needs the newest" sets as the oldest version: the newest stable one, or the newest one when none is stable.</summary>
    public static ModVersionMetadata? Newest(ContentIndexListing listing)
    {
        var releases = Releases(listing);
        return releases.FirstOrDefault(release => release.ReleaseStatus == ReleaseStatus.Stable) ?? releases.FirstOrDefault();
    }

    /// <summary>
    /// The dependencies that the mod.toml of the newest stamped release of a listing declares, or null when the listing
    /// has no release that is not yanked. An entry the author bounded is stamped as authored, so it is not among them.
    /// </summary>
    public static ListingDeclaredDependencies? Derived(ContentIndexSnapshot snapshot, string id)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var listing = snapshot.Listings.FirstOrDefault(entry => ModIds.Equals(entry.Id, id));
        if (listing is null || Releases(listing).FirstOrDefault() is not { } newest)
            return null;

        var declared = newest.Dependencies
            .Where(dependency => dependency.Source == MetadataSource.Derived && !dependency.IsAnyOf)
            .Select(dependency => new ListingDeclaredDependency(dependency.ModId!, dependency.Kind == ModDependencyKind.Optional ? Optional : Required))
            .ToList();
        return new ListingDeclaredDependencies(newest.Version.ToString(), declared);
    }

    /// <summary>The dependencies a mod.toml declares, with the kinds the stamper gives them.</summary>
    public static ListingDeclaredDependencies FromModToml(string release, IReadOnlyList<LocalModDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        return new ListingDeclaredDependencies(release, dependencies.Select(dependency => new ListingDeclaredDependency(dependency.ModId, dependency.Optional ? Optional : Required)).ToList());
    }

    private static bool IsCandidate(ContentIndexListing listing) =>
        listing.Authored?.Type is ContentType.Mod or ContentType.ModLoader && listing.IndexStatus?.State != IndexStatusState.Delisted;
}
