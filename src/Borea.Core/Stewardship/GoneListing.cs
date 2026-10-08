using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>
/// The releases of one listing that carry download.unavailable_since, because the watcher found their archive gone from its host
/// (RFC 0078). Only the watcher writes and removes that mark, so a steward reads it and no amendment changes it.
/// </summary>
/// <param name="Name">The name in the authored document of the listing, or null when the snapshot has no authored document for it.</param>
/// <param name="Releases">The marked releases, the newest mark first.</param>
public sealed record GoneListing(string ListingId, string? Name, IReadOnlyList<GoneRelease> Releases)
{
    /// <summary>Each listing of the snapshot that has a marked release, the listing with the newest mark first.</summary>
    public static IReadOnlyList<GoneListing> From(ContentIndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return [.. snapshot.Listings
            .Select(listing => new GoneListing(listing.Id, listing.Authored?.Name, [.. listing.Releases
                .Where(release => release.Download.UnavailableSince is not null)
                .Select(release => new GoneRelease(release.Version, release.Download.UnavailableSince!.Value))
                .OrderByDescending(release => release.Since)
                .ThenByDescending(release => release.Version)]))
            .Where(listing => listing.Releases.Count > 0)
            .OrderByDescending(listing => listing.Releases[0].Since)
            .ThenBy(listing => listing.ListingId, StringComparer.OrdinalIgnoreCase)];
    }
}

/// <param name="Since">When the watcher marked the release, as download.unavailable_since says.</param>
public sealed record GoneRelease(ModVersion Version, DateTimeOffset Since);
