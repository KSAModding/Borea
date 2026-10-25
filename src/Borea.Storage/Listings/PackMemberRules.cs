using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Storage.Mods;

namespace Borea.Storage.Listings;

/// <summary>
/// The member rules of RFC 0080 and RFC 0085 against the snapshot, in the words of check_members and member_notes in
/// tools/check_index.py of content-index. schemas/pack-vectors.json of content-index holds the cases, and the tests run a copy of it.
/// </summary>
internal static class PackMemberRules
{
    private const string MemberSection = "mods";

    private static readonly (string Section, ContentType Type)[] PinnedSections = [(MemberSection, ContentType.Mod), ("vehicles", ContentType.Vehicle), ("saves", ContentType.Save)];

    public static IEnumerable<ListingIssue> Check(AuthoredTable document, ContentIndexSnapshot snapshot)
    {
        if (document.GetString("type") != ListingDraft.ModPackType)
            return [];

        var pins = Pins(document).ToList();
        var issues = new List<ListingIssue>();
        var stamped = new List<(string Where, ListingPackMember Pin, ModVersionMetadata Release)>();
        foreach (var (section, where, pin) in pins)
        {
            var (problem, release) = Member(section, pin, snapshot);
            if (problem is not null)
                issues.Add(Error(where, problem));
            else if (release is not null)
                stamped.Add((where, pin, release));
        }

        // A pinned vehicle or save needs its mods under [[mods]], so only those pins meet a dependency.
        var pinned = ListingPackDependencies.Pinned(pins.Where(entry => entry.Section == MemberSection).Select(entry => entry.Pin));
        foreach (var (where, pin, release) in stamped)
            issues.AddRange(ListingPackDependencies.Incomplete(pin, release, pinned).Select(problem => Error(where, problem)));

        foreach (var (section, where, pin) in pins)
        {
            if (Target(snapshot, pin.Id) is { IndexStatus.State: IndexStatusState.Disputed } target && target.Authored?.Type == pin.Type)
                issues.Add(new ListingIssue(ListingIssueSeverity.Note, where, $"'{pin.Id}' is disputed, and a client warns about it"));
        }

        return issues;
    }

    /// <summary>The pins that name an id and a version, each with its section and place.</summary>
    private static IEnumerable<(string Section, string Where, ListingPackMember Pin)> Pins(AuthoredTable document)
    {
        foreach (var (section, type) in PinnedSections)
        {
            var entries = document.GetList(section) ?? [];
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index] is AuthoredTable entry && entry.GetString("id") is { } id && entry.GetString("version") is { } version)
                    yield return (section, $"{section}[{index}]", new ListingPackMember(id, version) { Type = type });
            }
        }
    }

    /// <summary>Why a pin is refused, or else the release it pins when there is one to read.</summary>
    private static (string? Problem, ModVersionMetadata? Release) Member(string section, ListingPackMember pin, ContentIndexSnapshot snapshot)
    {
        var (id, version) = (pin.Id, pin.Version);
        var wanted = MetadataEnumMapper.ToDto(pin.Type);
        var pinner = section == MemberSection ? "a pack" : $"[[{section}]]";
        var listing = Target(snapshot, id);
        if (listing is null)
        {
            // The checks report a pin of a pack as a reference that does not nest.
            return snapshot.Packs.Any(pack => ModIds.Equals(pack.Id, id)) ? (null, null) : ($"'{id}' is not a listed {wanted}, and {pinner} pins only listed {wanted}s", null);
        }

        if (listing.IndexStatus?.State == IndexStatusState.Delisted)
            return ($"'{id}' is delisted, and {pinner} pins only listed {wanted}s", null);
        if (listing.Authored?.Type is { } type && type != pin.Type)
            return ($"'{id}' is listed as a {MetadataEnumMapper.ToDto(type)}, and {pinner} pins only {wanted}s", null);

        // The schema reports a version that is not SemVer.
        if (!ModVersion.TryParse(version, out _))
            return (null, null);

        var release = listing.Releases.FirstOrDefault(release => release.Version.ToString() == version);
        if (release is null)
            return ($"'{id}' has no stamped release {version}", null);
        return release.Yanked ? ($"'{id}' {version} is yanked", null) : (null, release);
    }

    /// <summary>The listing an id names in any of the lists of the snapshot, delisted ones included, as the checks find it.</summary>
    private static ContentIndexListing? Target(ContentIndexSnapshot snapshot, string id) =>
        snapshot.Listings.Concat(snapshot.Vehicles).Concat(snapshot.Saves).FirstOrDefault(listing => ModIds.Equals(listing.Id, id));

    private static ListingIssue Error(string location, string message) => new(ListingIssueSeverity.Error, location, message);
}
