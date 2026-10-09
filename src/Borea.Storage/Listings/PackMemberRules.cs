using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Storage.Mods;

namespace Borea.Storage.Listings;

/// <summary>
/// The member rules of RFC 0080 against the snapshot, in the words of check_members and member_notes in tools/check_index.py of
/// content-index. schemas/pack-vectors.json of content-index holds the cases, and the tests run a copy of it.
/// </summary>
internal static class PackMemberRules
{
    private const string MemberSection = "mods";

    private static readonly string[] PinnedSections = [MemberSection, "vehicles", "saves"];

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

        var pinned = ListingPackDependencies.Pinned(pins.Where(entry => entry.Section == MemberSection).Select(entry => entry.Pin));
        foreach (var (where, pin, release) in stamped)
            issues.AddRange(ListingPackDependencies.Incomplete(pin, release, pinned).Select(problem => Error(where, problem)));

        foreach (var (section, where, pin) in pins)
        {
            if (section == MemberSection && Listing(snapshot, pin.Id) is { Authored.Type: ContentType.Mod, IndexStatus.State: IndexStatusState.Disputed })
                issues.Add(new ListingIssue(ListingIssueSeverity.Note, where, $"'{pin.Id}' is disputed, and a client warns about it"));
        }

        return issues;
    }

    /// <summary>The pins that name an id and a version, each with its section and place.</summary>
    private static IEnumerable<(string Section, string Where, ListingPackMember Pin)> Pins(AuthoredTable document)
    {
        foreach (var section in PinnedSections)
        {
            var entries = document.GetList(section) ?? [];
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index] is AuthoredTable entry && entry.GetString("id") is { } id && entry.GetString("version") is { } version)
                    yield return (section, $"{section}[{index}]", new ListingPackMember(id, version));
            }
        }
    }

    /// <summary>Why a pin is refused, or else the release it pins when there is one to read.</summary>
    private static (string? Problem, ModVersionMetadata? Release) Member(string section, ListingPackMember pin, ContentIndexSnapshot snapshot)
    {
        var (id, version) = (pin.Id, pin.Version);
        if (section != MemberSection)
            return ($"'{id}' cannot be pinned, because no content type for {section} is defined yet", null);

        var listing = Listing(snapshot, id);
        if (listing is null)
        {
            // The checks report a pin of a pack as a reference that does not nest.
            return snapshot.Packs.Any(pack => ModIds.Equals(pack.Id, id)) ? (null, null) : ($"'{id}' is not a listed mod, and a pack pins only listed mods", null);
        }

        if (listing.IndexStatus?.State == IndexStatusState.Delisted)
            return ($"'{id}' is delisted, and a pack pins only listed mods", null);
        if (listing.Authored?.Type is { } type and not ContentType.Mod)
            return ($"'{id}' is listed as a {MetadataEnumMapper.ToDto(type)}, and a pack pins only mods", null);

        // The schema reports a version that is not SemVer.
        if (!ModVersion.TryParse(version, out _))
            return (null, null);

        var release = listing.Releases.FirstOrDefault(release => release.Version.ToString() == version);
        if (release is null)
            return ($"'{id}' has no stamped release {version}", null);
        return release.Yanked ? ($"'{id}' {version} is yanked", null) : (null, release);
    }

    /// <summary>The listing an id names, delisted ones included, as the checks find it.</summary>
    private static ContentIndexListing? Listing(ContentIndexSnapshot snapshot, string id) =>
        snapshot.Listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, id));

    private static ListingIssue Error(string location, string message) => new(ListingIssueSeverity.Error, location, message);
}
