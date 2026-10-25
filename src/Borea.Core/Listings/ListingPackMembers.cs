using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Listings;

/// <summary>
/// What a pack version can pin: listings of the type of the pack section that are not delisted, a mod under [[mods]], a vehicle
/// under [[vehicles]] and a save under [[saves]] (RFC 0085), at stamped releases that are not yanked and whose download is not
/// gone (RFC 0078). A pin to anything else is one that no client can install.
/// </summary>
public static class ListingPackMembers
{
    /// <summary>The listings of one type that have at least one release to pin, in the order of the snapshot.</summary>
    public static IReadOnlyList<ContentIndexListing> Candidates(ContentIndexSnapshot snapshot, ContentType type = ContentType.Mod)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Of(snapshot, type).Where(listing => IsCandidate(listing, type) && Offered(listing).Count > 0).ToList();
    }

    /// <summary>The releases of a listing that a new pin can name, newest first.</summary>
    public static IReadOnlyList<ModVersionMetadata> Offered(ContentIndexListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return listing.Authored is { } authored && IsCandidate(listing, authored.Type)
            ? listing.Releases.Where(release => release.IsOffered).OrderByDescending(release => release.Version).ToList()
            : [];
    }

    /// <summary>The listing of that type a pin names, or null when the snapshot has no such listing to pin.</summary>
    public static ContentIndexListing? Listing(ContentIndexSnapshot snapshot, string id, ContentType type = ContentType.Mod)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Of(snapshot, type).FirstOrDefault(listing => ModIds.Equals(listing.Id, id) && IsCandidate(listing, type));
    }

    /// <summary>The release a pin names, when a pin can name it.</summary>
    public static ModVersionMetadata? Release(ContentIndexSnapshot snapshot, ListingPackMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (Listing(snapshot, member.Id, member.Type) is not { } listing || !ModVersion.TryParse(member.Version, out var version))
            return null;

        return Offered(listing).FirstOrDefault(release => release.Version == version);
    }

    /// <summary>
    /// The release a pin names when it is not yanked, also when its download is gone, because a pin of it stays in the file.
    /// </summary>
    public static ModVersionMetadata? Pinned(ContentIndexSnapshot snapshot, ListingPackMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (Listing(snapshot, member.Id, member.Type) is not { } listing || !ModVersion.TryParse(member.Version, out var version))
            return null;

        return listing.Releases.FirstOrDefault(release => !release.Yanked && release.Version == version);
    }

    /// <summary>
    /// The pinned release with the highest game_min, which is the game_min the pack needs, or null when no pin names a release.
    /// A gone download does not change what a release needs, so its pin counts. Only the revision orders game versions, so it decides.
    /// </summary>
    public static ModVersionMetadata? HighestGameMin(ContentIndexSnapshot snapshot, IEnumerable<ListingPackMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return members.Select(member => Pinned(snapshot, member)).OfType<ModVersionMetadata>().MaxBy(release => release.GameMinRevision);
    }

    /// <summary>The list of the snapshot that holds listings of that type.</summary>
    public static IReadOnlyList<ContentIndexListing> Of(ContentIndexSnapshot snapshot, ContentType type)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return type switch
        {
            ContentType.Vehicle => snapshot.Vehicles,
            ContentType.Save => snapshot.Saves,
            _ => snapshot.Listings,
        };
    }

    private static bool IsCandidate(ContentIndexListing listing, ContentType type) =>
        type is ContentType.Mod or ContentType.Vehicle or ContentType.Save
        && listing.Authored?.Type == type && listing.IndexStatus?.State != IndexStatusState.Delisted;
}
