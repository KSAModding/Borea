using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>
/// The numbers of the Statistics tab, counted from one snapshot. They are about content and never about people, so no row carries an author.
/// They cover what a player can see, so a delisted entry, an entry without an authored document and a pack without a version that is not retracted do not count.
/// </summary>
/// <param name="CurrentBuild">The newest build in game_versions of the snapshot, or null when it names none. Without it, no listing counts as with or without a release for the build.</param>
/// <param name="ModsOnCurrentBuild">The mods that fit the current build.</param>
/// <param name="ModsNotOnCurrentBuild">The other mods, by name.</param>
/// <param name="MostDependencies">The listings with the most direct dependencies, the most first.</param>
/// <param name="MostDependedOn">The ids that the most listings depend on directly, the most first.</param>
/// <param name="NoCurrentRelease">The content of every type that does not fit the current build, by name.</param>
/// <param name="NoRecentUpdate">The listings whose last update is older than <see cref="NoUpdateDays"/>, by name. A listing whose date the index does not report is not in it.</param>
/// <param name="NoForumsThread">The listings whose type requires a forums thread and whose forums link names no thread, by name.</param>
/// <param name="ReleaseHosts">The hosts of the [releases] table. A listing whose releases enter the index by pull request has none.</param>
public sealed record IndexStatistics(
    GameVersion? CurrentBuild,
    int ModsOnCurrentBuild,
    IReadOnlyList<IndexStatistics.Listing> ModsNotOnCurrentBuild,
    IReadOnlyList<IndexStatistics.DependencyCount> MostDependencies,
    IReadOnlyList<IndexStatistics.DependencyCount> MostDependedOn,
    IReadOnlyList<IndexStatistics.Listing> NoCurrentRelease,
    IReadOnlyList<IndexStatistics.Listing> NoRecentUpdate,
    IReadOnlyList<IndexStatistics.Listing> NoImage,
    IReadOnlyList<IndexStatistics.Listing> NoForumsThread,
    IReadOnlyList<IndexStatistics.TypeCount> Types,
    IReadOnlyList<IndexStatistics.NameCount> Tags,
    IReadOnlyList<IndexStatistics.NameCount> Licenses,
    IReadOnlyList<IndexStatistics.NameCount> ReleaseHosts,
    IReadOnlyList<IndexStatistics.DownloadCount> MostDownloaded)
{
    /// <summary>How long a listing can go without an update before the upkeep card names it.</summary>
    public const int NoUpdateDays = 180;

    /// <summary>The length of each list that ranks listings. A list of what needs upkeep is never cut.</summary>
    public const int RankedRows = 10;

    public sealed record Listing(string Id, string Name);

    /// <param name="Required">The required entries of the listing, or the listings that require this id.</param>
    /// <param name="Optional">The optional, recommended and suggested entries of the listing, or the listings that have this id only as one of those.</param>
    /// <param name="IsListed">False for an id that a listing depends on and that names no entry of these numbers, so it has no content page.</param>
    public sealed record DependencyCount(string Id, string Name, int Required, int Optional, bool IsListed);

    public sealed record TypeCount(ContentType Type, int Count);

    public sealed record NameCount(string Name, int Count);

    public sealed record DownloadCount(string Id, string Name, long Total);

    private sealed record Entry(string Id, string Name, ContentType Type, IReadOnlyList<string> Tags, string License, string? ForumUrl, ContentImages? Images, DateTimeOffset? UpdatedAt, GameCompatibility Compatibility);

    private sealed record DirectDependency(IReadOnlyList<string> Ids, bool IsRequired);

    public static IndexStatistics From(ContentIndexSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var releases = GameReleaseList.From(snapshot.GameVersions);
        var build = releases.Newest;
        var listings = snapshot.Listings.Concat(snapshot.Vehicles).Concat(snapshot.Saves)
            .Where(listing => listing.Authored is { Type: ContentType.Mod or ContentType.ModLoader or ContentType.Vehicle or ContentType.Save } && listing.IndexStatus?.State != IndexStatusState.Delisted)
            .ToList();
        var entries = listings.Select(listing => EntryOf(listing, build))
            .Concat(snapshot.Packs.Select(pack => EntryOf(snapshot, pack, build, releases)).OfType<Entry>())
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = entries.ToLookup(entry => entry.Id, entry => entry.Name, ModIds.Comparer);
        var dependencies = listings.Select(listing => (listing.Id, Direct: DirectDependencies(listing))).ToList();
        var mods = entries.Where(entry => entry.Type == ContentType.Mod).ToList();

        return new IndexStatistics(
            build,
            mods.Count(entry => entry.Compatibility == GameCompatibility.Compatible),
            Rows(mods.Where(entry => entry.Compatibility == GameCompatibility.Incompatible)),
            Ranked(dependencies.Select(listing => new DependencyCount(
                listing.Id,
                names[listing.Id].First(),
                listing.Direct.Count(dependency => dependency.IsRequired),
                listing.Direct.Count(dependency => !dependency.IsRequired),
                IsListed: true))),
            Ranked(DependedOn(dependencies.Select(listing => listing.Direct), names)),
            Rows(entries.Where(entry => entry.Compatibility == GameCompatibility.Incompatible)),
            Rows(entries.Where(entry => now - entry.UpdatedAt > TimeSpan.FromDays(NoUpdateDays))),
            Rows(entries.Where(entry => entry.Images is null or { Icon: null, Description.Count: 0 })),
            Rows(entries.Where(entry => !entry.Type.IsVehicleOrSave() && ForumsThreadLink.ThreadOf(entry.ForumUrl) is null)),
            [.. new[] { ContentType.Mod, ContentType.ModLoader, ContentType.ModPack, ContentType.Vehicle, ContentType.Save }
                .Select(type => new TypeCount(type, entries.Count(entry => entry.Type == type)))],
            Counted(entries.SelectMany(entry => entry.Tags.Distinct(StringComparer.OrdinalIgnoreCase))),
            Counted(entries.Select(entry => entry.License)),
            Counted(listings.SelectMany(listing => listing.Authored!.Releases?.Hosts.Select(host => host.Host.ToLowerInvariant()) ?? [])),
            [.. listings.Where(listing => listing.Downloads is not null)
                .Select(listing => new DownloadCount(listing.Id, names[listing.Id].First(), listing.Downloads!.Total))
                .OrderByDescending(listing => listing.Total)
                .ThenBy(listing => listing.Name, StringComparer.OrdinalIgnoreCase)
                .Take(RankedRows)]);
    }

    /// <summary>A listing fits the build when one release that Borea offers fits it, whichever channel that release is in.</summary>
    private static Entry EntryOf(ContentIndexListing listing, GameVersion? build)
    {
        var authored = listing.Authored!;
        var compatibility = build is null ? GameCompatibility.Unknown
            : listing.Releases.Any(release => release.IsOffered && Compatibility.Evaluate(release, build) == GameCompatibility.Compatible) ? GameCompatibility.Compatible
            : GameCompatibility.Incompatible;
        return new Entry(listing.Id, authored.Name, authored.Type, authored.Tags, authored.License, authored.ForumUrl, listing.Images, listing.UpdatedAt, compatibility);
    }

    /// <summary>The newest version that is not retracted stands for the pack, the version that Discover rates by its bounds and its pins.</summary>
    private static Entry? EntryOf(ContentIndexSnapshot snapshot, ContentIndexPack pack, GameVersion? build, GameReleaseList releases)
    {
        if (pack.IndexStatus?.State == IndexStatusState.Delisted
            || pack.Versions.Where(version => version.IndexStatus?.State != IndexStatusState.Retracted).MaxBy(version => version.Metadata.Version) is not { Metadata: var metadata } newest)
            return null;

        var pinned = metadata.Mods
            .Select(pin => ListingPackMembers.Pinned(snapshot, new ListingPackMember(pin.ContentId, pin.Version.ToString())))
            .OfType<ModVersionMetadata>();
        return new Entry(pack.Id, metadata.Name, ContentType.ModPack, metadata.Tags, metadata.License, metadata.ForumUrl, newest.Images, pack.UpdatedAt, Compatibility.Evaluate(metadata, pinned, build, releases));
    }

    /// <summary>
    /// The entries of the newest release that Borea offers, or of the authored document without such a release.
    /// An any_of entry counts as one, and a conflict is no dependency.
    /// The [loader] table is not read, because almost every code mod has one, so it would push the listings with real dependencies out of the ranked lists.
    /// </summary>
    private static List<DirectDependency> DirectDependencies(ContentIndexListing listing)
    {
        var newest = listing.Releases.Where(release => release.IsOffered).MaxBy(release => release.Version);
        var entries = newest is null ? listing.Authored!.Dependencies : newest.Dependencies;
        return
        [
            .. entries
                .Where(entry => entry.Kind is not (ModDependencyKind.Conflict or ModDependencyKind.Unknown))
                .Select(entry => new DirectDependency(entry.IsAnyOf ? [.. entry.AnyOf.Select(alternative => alternative.ModId)] : [entry.ModId], entry.Kind == ModDependencyKind.Required)),
        ];
    }

    /// <summary>Each alternative of an any_of entry counts the listing once, and a listing that names an id twice counts it as required when one of the entries requires it.</summary>
    private static IEnumerable<DependencyCount> DependedOn(IEnumerable<IReadOnlyList<DirectDependency>> dependents, ILookup<string, string> names)
    {
        var counts = new Dictionary<string, (int Required, int Optional)>(ModIds.Comparer);
        foreach (var direct in dependents)
        {
            var required = direct.Where(entry => entry.IsRequired).SelectMany(entry => entry.Ids).ToHashSet(ModIds.Comparer);
            var optional = direct.Where(entry => !entry.IsRequired).SelectMany(entry => entry.Ids).Where(id => !required.Contains(id)).ToHashSet(ModIds.Comparer);
            foreach (var id in required)
                counts[id] = (counts.GetValueOrDefault(id).Required + 1, counts.GetValueOrDefault(id).Optional);
            foreach (var id in optional)
                counts[id] = (counts.GetValueOrDefault(id).Required, counts.GetValueOrDefault(id).Optional + 1);
        }

        return counts.Select(count => new DependencyCount(count.Key, names[count.Key].FirstOrDefault(count.Key), count.Value.Required, count.Value.Optional, names.Contains(count.Key)));
    }

    private static List<Listing> Rows(IEnumerable<Entry> entries) => [.. entries.Select(entry => new Listing(entry.Id, entry.Name))];

    private static List<DependencyCount> Ranked(IEnumerable<DependencyCount> rows) =>
    [
        .. rows.Where(row => row.Required + row.Optional > 0)
            .OrderByDescending(row => row.Required + row.Optional)
            .ThenByDescending(row => row.Required)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(RankedRows),
    ];

    private static List<NameCount> Counted(IEnumerable<string> names) =>
    [
        .. names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new NameCount(group.Key, group.Count()))
            .OrderByDescending(count => count.Count)
            .ThenBy(count => count.Name, StringComparer.OrdinalIgnoreCase),
    ];
}
