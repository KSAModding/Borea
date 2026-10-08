using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Listings;

/// <summary>One choice of a dependency of a stamped release: a mod id with its inclusive bounds.</summary>
public sealed record ListingPackOption(string Id, ModVersion? Min, ModVersion? Max)
{
    public bool Contains(ModVersion version) => (Min is not { } min || min <= version) && (Max is not { } max || version <= max);
}

public enum ListingPackNeedReason
{
    /// <summary>The release needs one of several mods, of which at least one is listed, and the author chooses which.</summary>
    Choose = 0,

    /// <summary>No choice of the dependency is a listed mod, so no pack can pin the release.</summary>
    NotListed = 1,

    /// <summary>No stable release of the mod is inside the bounds of every member that requires it.</summary>
    NoRelease = 2,
}

/// <summary>A required dependency of a member that "Add the missing dependencies" cannot add.</summary>
public sealed record ListingPackNeed(ListingPackMember Member, IReadOnlyList<ListingPackOption> Options, ListingPackNeedReason Reason)
{
    /// <summary>The need in the words of the checks of content-index, such as "'Alpha' 1.0.0 requires 'Beta' 2.0.0 or newer".</summary>
    public string Text => ListingPackDependencies.Requires(Member, Options);
}

/// <param name="Pins">The pins to append, in order.</param>
/// <param name="Needs">What the author still has to settle.</param>
public sealed record ListingPackFix(IReadOnlyList<ListingPackMember> Pins, IReadOnlyList<ListingPackNeed> Needs);

/// <summary>
/// The required dependencies of pinned releases, as the member rules of RFC 0080 read them, and the pins that meet them.
/// It follows check_members of tools/check_index.py and site/js/pack.js of content-index, which schemas/pack-vectors.json tests.
/// </summary>
public static class ListingPackDependencies
{
    /// <summary>The stamped release a pin names, yanked ones included, when the pin names a listed mod.</summary>
    public static ModVersionMetadata? Stamped(ContentIndexSnapshot snapshot, string id, string version)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ListingPackMembers.Listing(snapshot, id)?.Releases.FirstOrDefault(release => release.Version.ToString() == version);
    }

    /// <summary>The required dependencies of a release that is not yanked, each as its choices.</summary>
    public static IReadOnlyList<IReadOnlyList<ListingPackOption>> Required(ModVersionMetadata? release) => release is null || release.Yanked
        ? []
        : release.Dependencies.Where(dependency => dependency.Kind == ModDependencyKind.Required).Select(Options).Where(options => options.Count > 0).ToList();

    public static IReadOnlyList<ListingPackOption> Options(ModDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        return dependency.AnyOf is { } alternatives
            ? alternatives.Select(alternative => new ListingPackOption(alternative.ModId, alternative.MinVersion, alternative.MaxVersion)).ToList()
            : [new ListingPackOption(dependency.ModId!, dependency.MinVersion, dependency.MaxVersion)];
    }

    /// <summary>
    /// The required dependencies of a release where no choice is a listed mod, so no pack can pin the release with a complete set.
    /// Each entry holds the ids of the choices.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> UnlistedNeeds(ContentIndexSnapshot snapshot, ModVersionMetadata? release)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Required(release)
            .Where(options => !options.Any(option => ListingPackMembers.Listing(snapshot, option.Id) is not null))
            .Select(options => (IReadOnlyList<string>)options.Select(option => option.Id).ToList())
            .ToList();
    }

    /// <summary>
    /// The release a new pin starts on: the newest stable release that can be pinned, else the newest release that can be pinned,
    /// else the newest stable release, else the newest release.
    /// </summary>
    public static ModVersionMetadata? DefaultRelease(ContentIndexSnapshot snapshot, ContentIndexListing listing)
    {
        var offered = ListingPackMembers.Offered(listing);
        var pinnable = offered.Where(release => UnlistedNeeds(snapshot, release).Count == 0).ToList();
        var choices = pinnable.Count > 0 ? pinnable : offered;
        return choices.FirstOrDefault(release => release.ReleaseStatus == ReleaseStatus.Stable) ?? choices.FirstOrDefault();
    }

    /// <summary>The first pin of each id, as the checks key them.</summary>
    public static IReadOnlyDictionary<string, ListingPackMember> Pinned(IEnumerable<ListingPackMember> pins)
    {
        var pinned = new Dictionary<string, ListingPackMember>(ModIds.Comparer);
        foreach (var pin in pins)
            pinned.TryAdd(pin.Id, pin);
        return pinned;
    }

    public static bool PinnedWithin(ListingPackOption option, IReadOnlyDictionary<string, ListingPackMember> pinned) =>
        pinned.TryGetValue(option.Id, out var pin) && ModVersion.TryParse(pin.Version, out var version) && option.Contains(version);

    public static string Named(ListingPackMember pin) => $"'{pin.Id}' {pin.Version}";

    /// <summary>The choices of a dependency, such as "'Beta' 1.0.0 to 2.0.0" or "one of 'Beta', 'Gamma'".</summary>
    public static string Wanted(IReadOnlyList<ListingPackOption> options)
    {
        var described = options.Select(option => $"'{option.Id}'{Bounds(option)}").ToList();
        return described.Count == 1 ? described[0] : $"one of {string.Join(", ", described)}";
    }

    public static string Requires(ListingPackMember pin, IReadOnlyList<ListingPackOption> options) => $"{Named(pin)} requires {Wanted(options)}";

    /// <summary>
    /// The required dependencies of a pinned release that the pins do not meet, and its conflicts that a pin matches, in the words
    /// of _incomplete in tools/check_index.py.
    /// </summary>
    public static IEnumerable<string> Incomplete(ListingPackMember pin, ModVersionMetadata release, IReadOnlyDictionary<string, ListingPackMember> pinned)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(pinned);
        foreach (var dependency in release.Dependencies)
        {
            var options = Options(dependency);
            if (dependency.Kind == ModDependencyKind.Required && !options.Any(option => PinnedWithin(option, pinned)))
                yield return $"{Requires(pin, options)}, and the pack {PinnedInstead(options, pinned)}";
            else if (IsMatchedConflict(dependency, pin, options, pinned))
                yield return $"{Named(pin)} conflicts with {Wanted(options)}, and the pack pins {Named(pinned[dependency.ModId!])}";
        }
    }

    /// <summary>
    /// Whether a pin fits the other pins: no pack is kept from pinning its release, the pins meet its required dependencies, it is inside
    /// the bounds every other pinned release sets for its mod, and no conflict on either side matches.
    /// </summary>
    public static bool Fits(ContentIndexSnapshot snapshot, IReadOnlyList<ListingPackMember> pins, ListingPackMember pin)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(pin);
        if (Stamped(snapshot, pin.Id, pin.Version) is not { Yanked: false } release || UnlistedNeeds(snapshot, release).Count > 0)
            return false;

        var others = pins.Where(other => !ModIds.Equals(other.Id, pin.Id)).ToList();
        var pinned = Pinned(others.Prepend(pin));
        if (Incomplete(pin, release, pinned).Any())
            return false;

        foreach (var other in others)
        {
            if (Stamped(snapshot, other.Id, other.Version) is not { Yanked: false } otherRelease)
                continue;

            foreach (var dependency in otherRelease.Dependencies)
            {
                var options = Options(dependency);
                if (!options.Any(option => ModIds.Equals(option.Id, pin.Id)))
                    continue;
                if (dependency.Kind == ModDependencyKind.Required && !options.Any(option => PinnedWithin(option, pinned)))
                    return false;
                if (IsMatchedConflict(dependency, other, options, pinned))
                    return false;
            }
        }

        return true;
    }

    /// <summary>A conflict with a single mod other than the pin itself, which a pin inside its bounds matches.</summary>
    private static bool IsMatchedConflict(ModDependency dependency, ListingPackMember pin, IReadOnlyList<ListingPackOption> options, IReadOnlyDictionary<string, ListingPackMember> pinned) =>
        dependency.Kind == ModDependencyKind.Conflict && !dependency.IsAnyOf && !ModIds.Equals(dependency.ModId, pin.Id) && PinnedWithin(options[0], pinned);

    private static string PinnedInstead(IReadOnlyList<ListingPackOption> options, IReadOnlyDictionary<string, ListingPackMember> pinned)
    {
        var found = options.Where(option => pinned.ContainsKey(option.Id)).Select(option => Named(pinned[option.Id])).ToList();
        if (found.Count > 0)
            return $"pins {string.Join(" and ", found)}";
        return options.Count == 1 ? "does not pin it" : "pins none of them";
    }

    /// <summary>
    /// What "Add the missing dependencies" adds: each required dependency that no written pin names, at the newest stable release
    /// inside the bounds of every pin that requires it. Each round chooses every added pin again from the needs of all pins,
    /// because a release added later can narrow the bounds of a mod added earlier. An any_of stays a need for the author.
    /// </summary>
    public static ListingPackFix Missing(ContentIndexSnapshot snapshot, IReadOnlyList<ListingPackMember> written)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(written);
        var names = written.Select(pin => pin.Id).ToHashSet(ModIds.Comparer);
        var rounds = snapshot.Listings.Count(listing => ListingPackMembers.Listing(snapshot, listing.Id) is not null) + 1;
        IReadOnlyList<ListingPackMember> added = [];
        for (var round = 0; round < rounds; round++)
        {
            var needed = new List<(ContentIndexListing Listing, List<ListingPackOption> Needs)>();
            foreach (var (_, options) in Requirements(snapshot, written.Concat(added)))
            {
                if (options.Count != 1 || names.Contains(options[0].Id) || ListingPackMembers.Listing(snapshot, options[0].Id) is not { } listing)
                    continue;

                var index = needed.FindIndex(entry => ModIds.Equals(entry.Listing.Id, listing.Id));
                if (index < 0)
                    needed.Add((listing, [options[0]]));
                else
                    needed[index].Needs.Add(options[0]);
            }

            var next = needed
                .Select(entry => (entry.Listing, Release: ListingPackMembers.Offered(entry.Listing)
                    .FirstOrDefault(release => release.ReleaseStatus == ReleaseStatus.Stable && entry.Needs.All(option => option.Contains(release.Version)))))
                .Where(entry => entry.Release is not null)
                .Select(entry => new ListingPackMember(entry.Listing.Id, entry.Release!.Version.ToString()))
                .ToList();
            if (next.Count == added.Count && next.All(pin => added.Any(other => ModIds.Equals(other.Id, pin.Id) && other.Version == pin.Version)))
                break;
            added = next;
        }

        var ids = written.Concat(added).Select(pin => pin.Id).ToHashSet(ModIds.Comparer);
        var needs = Requirements(snapshot, written.Concat(added))
            .Where(entry => !entry.Options.Any(option => ids.Contains(option.Id)))
            .Select(entry => new ListingPackNeed(entry.Pin, entry.Options, !entry.Options.Any(option => ListingPackMembers.Listing(snapshot, option.Id) is not null)
                ? ListingPackNeedReason.NotListed
                : entry.Options.Count > 1 ? ListingPackNeedReason.Choose : ListingPackNeedReason.NoRelease))
            .ToList();
        return new ListingPackFix(added, needs);
    }

    private static IEnumerable<(ListingPackMember Pin, IReadOnlyList<ListingPackOption> Options)> Requirements(ContentIndexSnapshot snapshot, IEnumerable<ListingPackMember> pins) =>
        pins.SelectMany(pin => Required(Stamped(snapshot, pin.Id, pin.Version)).Select(options => (pin, options)));

    private static string Bounds(ListingPackOption option) => (option.Min, option.Max) switch
    {
        ({ } low, { } high) => $" {low} to {high}",
        ({ } low, null) => $" {low} or newer",
        (null, { } high) => $" {high} or older",
        _ => string.Empty,
    };
}
