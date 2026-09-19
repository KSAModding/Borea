using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;

namespace Borea.Storage.Listings;

/// <summary>
/// The rules of the checks of content-index that the schema cannot express: check_schema.py, check_license.py,
/// check_index.py, check_tags.py, check_images.py and check_image_references.py, and the install root of the stamper.
/// </summary>
internal static partial class ListingRules
{
    public const int AbstractLimit = 280;

    private const string TagsSpecUrl = "https://github.com/KSAModding/content-manager-design/blob/main/spec/tags.md";

    public static IEnumerable<ListingIssue> Check(AuthoredTable document, ListingCheckContext context, SpdxLicenseList licenses)
    {
        var own = document.GetString("id");
        var issues = new List<ListingIssue>();
        CheckLinks(document, issues);
        CheckGameBounds(document, context.Snapshot, issues);
        CheckLicenses(document, licenses, issues);
        if (own is not null && ModIds.Equals(document.GetString("superseded_by"), own))
            issues.Add(Error("superseded_by", "a listing cannot supersede itself"));
        CheckLoader(document, own, issues);
        CheckDependencies(document, own, issues);
        CheckImages(document, issues);
        if (context.Snapshot is { } snapshot)
        {
            CheckIndex(document, own, context.ListedId, snapshot, issues);
            CheckTags(document, snapshot, issues);
        }

        if (document.GetString("abstract") is { } text && text.EnumerateRunes().Count() is > AbstractLimit and var length)
            issues.Add(Note("abstract", $"{length} characters is longer than {AbstractLimit}, and an abstract is one or two sentences for list views"));

        if (context.Archive is { } archive && own is not null)
            CheckArchive(document, own, archive, issues);

        return issues;
    }

    private static void CheckLinks(AuthoredTable document, List<ListingIssue> issues)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, _) in document.GetTable("links")?.Entries ?? [])
        {
            if (seen.TryGetValue(key, out var first))
                issues.Add(Error("links", $"'{key}' and '{first}' are the same key"));
            seen[key] = key;
        }
    }

    private static void CheckGameBounds(AuthoredTable document, ContentIndexSnapshot? snapshot, List<ListingIssue> issues)
    {
        if (document.GetTable("compatibility") is not { } compatibility)
            return;

        var min = compatibility.GetString("game_min");
        var max = compatibility.GetString("game_max");
        if (BoundKey(min) is { } low && BoundKey(max) is { } high && low.IsMonth == high.IsMonth && (high.Major, high.Minor).CompareTo((low.Major, low.Minor)) < 0)
            issues.Add(Error("compatibility", $"game_max '{max}' is older than game_min '{min}'"));

        var builds = snapshot?.GameVersions?.Versions;
        if (builds is not { Count: > 0 })
            return;

        foreach (var (name, bound) in new[] { ("game_min", min), ("game_max", max) })
        {
            if (bound is null || MonthBound().Match(bound) is not { Success: true } month)
                continue;

            var year = int.Parse(month.Groups[1].Value, CultureInfo.InvariantCulture);
            var number = int.TryParse(month.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue;
            var now = DateTimeOffset.UtcNow;
            if (name == "game_max" && (now.Year, now.Month).CompareTo((year, number)) <= 0)
                continue;

            if (!builds.Any(build => GameVersion.TryParse(build, out var version) && version.Year == year && version.Month == number))
                issues.Add(Error($"compatibility.{name}", $"'{bound}' names a month with no build in the game release list"));
        }
    }

    private static (bool IsMonth, BigInteger Major, BigInteger Minor)? BoundKey(string? bound)
    {
        if (bound is null)
            return null;

        if (RevisionBound().Match(bound) is { Success: true } revision)
            return (false, BigInteger.Parse(revision.Groups[1].Value, CultureInfo.InvariantCulture), BigInteger.Zero);

        return MonthBound().Match(bound) is { Success: true } month
            ? (true, BigInteger.Parse(month.Groups[1].Value, CultureInfo.InvariantCulture), BigInteger.Parse(month.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;
    }

    private static void CheckLicenses(AuthoredTable document, SpdxLicenseList licenses, List<ListingIssue> issues)
    {
        if (document.GetString("license") is { } license)
            issues.AddRange(licenses.Problems(license).Select(problem => Error("license", problem)));

        foreach (var (place, _, record) in ImageRecords(document))
        {
            if (record.GetString("license") is { } imageLicense)
                issues.AddRange(licenses.Problems(imageLicense).Select(problem => Error($"{place}.license", problem)));
        }
    }

    private static void CheckLoader(AuthoredTable document, string? own, List<ListingIssue> issues)
    {
        if (document.GetTable("loader") is not { } loader)
            return;

        CheckBounds("loader", loader, issues);
        if (own is not null && ModIds.Equals(loader.GetString("id"), own))
            issues.Add(Error("loader", "a listing cannot be its own loader"));
    }

    private static void CheckDependencies(AuthoredTable document, string? own, List<ListingIssue> issues)
    {
        var seen = new HashSet<string>(ModIds.Comparer);
        var entries = document.GetList("dependencies") ?? [];
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] is not AuthoredTable dependency)
                continue;

            var where = $"dependencies[{index}]";
            CheckBounds(where, dependency, issues);
            var alternatives = dependency.GetList("any_of");
            var members = alternatives?.OfType<AuthoredTable>().ToList() ?? [dependency];
            var local = new List<string>();
            for (var offset = 0; offset < members.Count; offset++)
            {
                if (alternatives is not null)
                    CheckBounds($"{where}.any_of[{offset}]", members[offset], issues);
                if (members[offset].GetString("id") is not { } id)
                    continue;
                if (!string.IsNullOrEmpty(own) && id.Length > 0 && ModIds.Equals(id, own))
                    issues.Add(Error(where, "a listing cannot depend on itself"));
                if (local.Contains(id, ModIds.Comparer))
                    issues.Add(Error(where, $"names '{id}' more than once"));
                else
                    local.Add(id);
            }

            foreach (var id in local)
            {
                if (!seen.Add(id))
                    issues.Add(Error(where, $"'{id}' already has a dependency entry"));
            }
        }
    }

    private static void CheckBounds(string where, AuthoredTable bounds, List<ListingIssue> issues)
    {
        var min = bounds.GetString("min");
        var max = bounds.GetString("max");
        if (ModVersion.TryParse(min, out var low) && ModVersion.TryParse(max, out var high) && high < low)
            issues.Add(Error(where, $"max '{max}' is below min '{min}'"));
    }

    private static void CheckImages(AuthoredTable document, List<ListingIssue> issues)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (place, role, record) in ImageRecords(document))
        {
            if (role == ListingImageRole.Icon && record["width"] is long width && record["height"] is long height
                && Math.Min(width, height) >= IconImage.MinShorterSidePixels
                && Math.Max(width, height) <= IconImage.MaxShorterSidePixels * IconImage.MaxSideRatio)
            {
                if (ListingImageMeasurement.OutsideLimits(role, width, height) is { } outside)
                {
                    issues.Add(Error(place, outside));
                }
                else if (width != height)
                {
                    var side = Math.Min(width, height);
                    var left = (width - side) / 2;
                    var top = (height - side) / 2;
                    issues.Add(Note(place, $"the icon is {width} by {height} pixels, so clients show the square from {left},{top} to {left + side},{top + side}"));
                }
            }

            if (role == ListingImageRole.Description && record.GetString("id") is { } id)
            {
                if (ids.TryGetValue(id, out var first))
                    issues.Add(Error(place, $"id '{id}' is already used by {first}"));
                else
                    ids[id] = place;
            }
        }

        var (destinations, html) = document.GetString("description") is { } description ? MarkdownImages.Scan(description) : ([], 0);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var destination in destinations)
        {
            if (!destination.StartsWith(MarkdownImages.Scheme, StringComparison.Ordinal))
            {
                issues.Add(Note("description", $"the image '{destination}' is not a {MarkdownImages.Scheme} reference, so no client shows it"));
                continue;
            }

            var id = destination[MarkdownImages.Scheme.Length..];
            referenced.Add(id);
            if (!ids.ContainsKey(id))
                issues.Add(Error("description", $"'{destination}' names no record in [[images.description]]"));
        }

        if (html > 0)
            issues.Add(Note("description", $"{html} image(s) in raw HTML, which no client shows"));

        foreach (var (id, place) in ids)
        {
            if (!referenced.Contains(id))
                issues.Add(Note(place, $"nothing in the description references '{id}', so no client shows it"));
        }
    }

    private static void CheckIndex(AuthoredTable document, string? own, string? listedId, ContentIndexSnapshot snapshot, List<ListingIssue> issues)
    {
        var holders = snapshot.Listings.Select(listing => (listing.Id, Type: listing.Authored?.Type, Forums: listing.Authored?.Links.GetValueOrDefault("forums")))
            .Concat(snapshot.Packs.Select(pack => (pack.Id, Type: (ContentType?)ContentType.ModPack, Forums: (string?)null)))
            .Where(holder => listedId is null || !ModIds.Equals(holder.Id, listedId))
            .ToList();

        if (own is not null && holders.FirstOrDefault(holder => ModIds.Equals(holder.Id, own)) is { Id: not null } taken)
            issues.Add(Error("id", $"the id '{own}' is already held by {Where(taken.Id, taken.Type)}, and ids compare case-insensitively"));

        var targets = holders.Where(holder => own is null || !ModIds.Equals(holder.Id, own)).ToList();
        Reference("superseded_by", document.GetString("superseded_by"), null, targets, issues);
        Reference("loader", document.GetTable("loader")?.GetString("id"), ContentType.ModLoader, targets, issues);
        var dependencies = document.GetList("dependencies") ?? [];
        for (var index = 0; index < dependencies.Count; index++)
        {
            if (dependencies[index] is not AuthoredTable dependency)
                continue;
            if (dependency.GetList("any_of") is { } alternatives)
            {
                for (var offset = 0; offset < alternatives.Count; offset++)
                    Reference($"dependencies[{index}].any_of[{offset}]", (alternatives[offset] as AuthoredTable)?.GetString("id"), ContentType.Mod, targets, issues);
            }
            else
            {
                Reference($"dependencies[{index}]", dependency.GetString("id"), ContentType.Mod, targets, issues);
            }
        }

        if (ForumsThreadLink.ThreadOf(document.GetTable("links")?.GetString("forums")) is { } thread)
        {
            var others = holders.Where(holder => ForumsThreadLink.ThreadOf(holder.Forums) == thread).Select(holder => Where(holder.Id, holder.Type)).Order(StringComparer.Ordinal).ToList();
            if (others.Count > 0)
                issues.Add(Note("links.forums", $"thread {thread} is also the forums thread of {string.Join(", ", others)}, so the thread cannot settle an id dispute between them"));
        }
    }

    private static void Reference(string where, string? value, ContentType? required, List<(string Id, ContentType? Type, string? Forums)> targets, List<ListingIssue> issues)
    {
        if (value is null || targets.FirstOrDefault(target => ModIds.Equals(target.Id, value)) is not { Id: not null } target)
            return;

        if (!string.Equals(value, target.Id, StringComparison.Ordinal))
            issues.Add(Error(where, $"'{value}' does not use the canonical id spelling '{target.Id}'"));

        if (required is { } type && target.Type is { } found && found != type)
        {
            var what = where == "loader" ? "a loader" : "a dependency";
            issues.Add(Error(where, $"'{value}' is listed as a {TypeName(found)}, and {what} has to be a {TypeName(type)}"));
        }
    }

    private static void CheckTags(AuthoredTable document, ContentIndexSnapshot snapshot, List<ListingIssue> issues)
    {
        var curated = snapshot.Tags.GetTags(ContentType.Mod).Select(tag => tag.Tag).ToHashSet(StringComparer.Ordinal);
        if (curated.Count == 0)
            return;

        var tags = document.GetList("tags")?.OfType<string>().ToList() ?? [];
        foreach (var tag in tags.Where(tag => !curated.Contains(tag)))
            issues.Add(Note("tags", $"'{tag}' is not a curated tag, so no client shows it as a filter; the list is at {TagsSpecUrl}"));

        if (!tags.Any(curated.Contains))
            issues.Add(Note("tags", $"the document has no curated tag, so no client can include it through a curated filter; the list is at {TagsSpecUrl}"));
    }

    private static void CheckArchive(AuthoredTable document, string own, ListingArchiveFacts archive, List<ListingIssue> issues)
    {
        if (document.GetString("type") != ListingDraft.ModType || document.GetTable("install")?.Contains("root") == true)
            return;

        if (archive.Root is not { } root)
        {
            issues.Add(Error("id", "the install root is neither derivable from the archive nor authored: the standard layout is one top-level directory "
                + $"containing mod.toml, named '{own}'"));
        }
        else if (string.Equals(root, own, StringComparison.OrdinalIgnoreCase) && root != own)
        {
            issues.Add(Error("id", $"the archive's top-level directory is '{root}' and the id is '{own}': the folder name is the identity the game sees, so the casing has to match"));
        }
        else if (root != own)
        {
            issues.Add(Error("id", $"the archive's top-level directory is '{root}', which does not match the id '{own}'"));
        }
    }

    internal static IEnumerable<(string Place, ListingImageRole Role, AuthoredTable Record)> ImageRecords(AuthoredTable document)
    {
        if (document.GetTable("images") is not { } images)
            yield break;

        if (images.GetTable("icon") is { } icon)
            yield return ("images.icon", ListingImageRole.Icon, icon);

        var entries = images.GetList("description") ?? [];
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] is AuthoredTable record)
                yield return ($"images.description[{index}]", ListingImageRole.Description, record);
        }
    }

    private static string Where(string id, ContentType? type) => type == ContentType.ModPack ? $"packs/{id}" : $"listings/{id}.toml";

    private static string TypeName(ContentType type) => type switch
    {
        ContentType.ModLoader => ListingDraft.ModLoaderType,
        ContentType.ModPack => "modpack",
        _ => ListingDraft.ModType,
    };

    private static ListingIssue Error(string location, string message) => new(ListingIssueSeverity.Error, location, message);

    private static ListingIssue Note(string location, string message) => new(ListingIssueSeverity.Note, location, message);

    [GeneratedRegex(@"^[0-9]{4}\.[0-9]+\.[0-9]+\.([0-9]+)(?![\s\S])")]
    private static partial Regex RevisionBound();

    [GeneratedRegex(@"^([0-9]{4})\.([0-9]+)(?![\s\S])")]
    private static partial Regex MonthBound();
}
