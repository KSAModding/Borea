using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Borea.Core.Game;
using Borea.Core.Mods;
using static Borea.Core.Stewardship.ReleaseJson;

namespace Borea.Core.Stewardship;

/// <summary>
/// The invariant of tools/check_amendment.py for one release file, the published version against the amended one, as
/// tools/amend.py runs it: without the archive and without the listing. So a change of the release notes, or the removal of an
/// authored dependency entry, is always refused here. The messages use the words of the Python checks.
/// </summary>
internal static partial class ReleaseAmendmentCheck
{
    internal static readonly string[] DependencyKinds = ["required", "optional", "recommends", "suggests", "conflict"];

    private static readonly HashSet<string> TopLevel =
    [
        "spec_version", "id", "type", "version", "version_scheme", "release_status", "release_date", "game_min", "game_min_revision",
        "game_max", "game_max_revision", "os", "download", "install_size", "install", "loader", "dependencies", "changelog",
        "changelog_text", "listing", "yanked", "yanked_reason",
    ];

    private static readonly string[] Immutable =
        ["spec_version", "id", "type", "version", "version_scheme", "release_status", "release_date", "download", "install_size", "install", "changelog", "listing"];

    internal static readonly string[] OsValues = ["windows", "linux", "macos"];
    private static readonly string[] WatcherDownloadKeys = ["mirrors", "unavailable_since"];
    private static readonly HashSet<string> LoaderKeys = ["id", "min", "max", "source"];
    private static readonly HashSet<string> DependencyKeys = ["id", "any_of", "kind", "min", "max", "source"];
    private static readonly HashSet<string> MemberKeys = ["id", "min", "max"];
    private static readonly string[] Sources = ["authored", "derived"];
    private static readonly string[] AnyOfKinds = ["required", "recommends"];

    /// <param name="path">The path of the file, releases/&lt;id&gt;/&lt;version&gt;.json.</param>
    /// <param name="errors">What the checks reject.</param>
    /// <param name="ownerOnly">What only the verified owner of the listing may change, because it widens the release.</param>
    public static void Check(string path, JsonObject published, JsonObject amended, List<string> errors, List<string> ownerOnly)
    {
        Unknown(amended, TopLevel, path, errors);
        CheckPath(path, amended, errors);
        CheckImmutable(published, amended, errors);
        CheckChangelogText(published, amended, errors);
        CheckOs(published, amended, errors, ownerOnly);
        CheckGameBounds(published, amended, errors, ownerOnly);
        CheckYank(published, amended, errors, ownerOnly);
        CheckLoader(published, amended, errors, ownerOnly);
        CheckDependencies(published, amended, errors, ownerOnly);
    }

    private static void Unknown(JsonObject mapping, HashSet<string> allowed, string what, List<string> errors)
    {
        foreach (var key in mapping.Select(member => member.Key).Where(key => !allowed.Contains(key)).Order(StringComparer.Ordinal))
            errors.Add($"{what} carries '{key}', which a release file does not have");
    }

    private static void CheckPath(string path, JsonObject amended, List<string> errors)
    {
        var match = ReleasePath().Match(path);
        if (!match.Success)
        {
            errors.Add($"{path} is not a release file");
            return;
        }

        var folder = match.Groups[1].Value;
        var stem = match.Groups[2].Value;
        if (!path.EndsWith(".json", StringComparison.Ordinal))
            errors.Add($"{path} does not end in a lowercase .json");
        if (stem.Contains('/', StringComparison.Ordinal))
        {
            errors.Add($"{path} sits below releases/<id>/, where a release file does not");
            return;
        }

        if (!ModIds.IsValid(folder))
            errors.Add($"{path} names '{folder}', which is not a valid content id");
        if (StringOf(amended["id"]) != folder)
            errors.Add($"{path} carries the id '{Show(amended["id"])}', and the folder says '{folder}'");
        if (StringOf(amended["version"]) != stem)
        {
            errors.Add($"{path} carries the version '{Show(amended["version"])}', and the file name says '{stem}'");
            return;
        }

        if (ModVersion.TryNormalizeAuthored(stem.Trim(), out var stored) && stored != stem)
            errors.Add($"{path} names the version '{stem}', which the index stores as '{stored}', so the file is releases/{folder}/{stored}.json with the version '{stored}'");
    }

    private static void CheckImmutable(JsonObject published, JsonObject amended, List<string> errors)
    {
        foreach (var key in Immutable)
        {
            if (JsonNode.DeepEquals(published[key], amended[key]))
                continue;

            errors.Add($"'{key}' changed, and identity, the version, the download and the install data never change after publish");
            if (key == "download" && WatcherKeysOnly(published[key], amended[key]) is { Length: > 0 } watched)
                errors.Add($"the download differs only in {watched}, which the watcher writes on the default branch: rebase and run tools/amend.py again");
        }
    }

    private static string? WatcherKeysOnly(JsonNode? published, JsonNode? amended)
    {
        if (published is not JsonObject before || amended is not JsonObject after)
            return null;

        JsonObject Rest(JsonObject download) => new(download.Where(member => !WatcherDownloadKeys.Contains(member.Key)).Select(member => KeyValuePair.Create(member.Key, member.Value?.DeepClone())));
        if (!JsonNode.DeepEquals(Rest(before), Rest(after)))
            return null;

        return string.Join(" and ", WatcherDownloadKeys.Where(key => !JsonNode.DeepEquals(before[key], after[key])).Select(key => $"'download.{key}'"));
    }

    private static void CheckChangelogText(JsonObject published, JsonObject amended, List<string> errors)
    {
        const string Key = "changelog_text";
        var change = !published.ContainsKey(Key) && amended.ContainsKey(Key) ? "is added"
            : published.ContainsKey(Key) && !amended.ContainsKey(Key) ? "is removed"
            : JsonNode.DeepEquals(published[Key], amended[Key]) ? null
            : "changed";
        if (change is not null)
            errors.Add($"'{Key}' {change}, and the listing was not read, so nothing shows whether the watcher writes it");
    }

    private static void CheckOs(JsonObject published, JsonObject amended, List<string> errors, List<string> ownerOnly)
    {
        if (JsonNode.DeepEquals(published["os"], amended["os"]))
            return;

        var platforms = amended["os"];
        if (platforms is not null && (platforms is not JsonArray list || list.Count == 0
            || list.Any(platform => !OsValues.Contains(StringOf(platform))) || list.Select(StringOf).Distinct().Count() != list.Count))
        {
            errors.Add($"os is absent or a list of distinct platforms from {string.Join(", ", OsValues)}");
            return;
        }

        ownerOnly.Add($"os changes from {Platforms(published)} to {Platforms(amended)}");
    }

    private static string Platforms(JsonObject document) =>
        document["os"] is JsonArray { Count: > 0 } platforms ? string.Join(", ", platforms.Select(Show)) : "no restriction";

    private static void CheckGameBounds(JsonObject published, JsonObject amended, List<string> errors, List<string> ownerOnly)
    {
        var publishedMin = IntegerOf(published["game_min_revision"]);
        var amendedMin = IntegerOf(amended["game_min_revision"]);
        if (publishedMin is null)
            errors.Add("the published version carries no game_min_revision, so the lower bound cannot be compared");
        if (amendedMin is null)
            errors.Add("game_min_revision is missing or is not a number");
        else if (amendedMin < publishedMin)
            ownerOnly.Add($"game_min_revision falls from {publishedMin} to {amendedMin}, which widens the release");

        var publishedMax = published["game_max_revision"];
        var amendedMax = amended["game_max_revision"];
        if (amendedMax is null)
        {
            if (publishedMax is not null)
                ownerOnly.Add($"game_max_revision {Show(publishedMax)} is removed, which widens the release");
        }
        else if (IntegerOf(amendedMax) is not { } max)
        {
            errors.Add("game_max_revision is not a number");
        }
        else if (max > IntegerOf(publishedMax))
        {
            ownerOnly.Add($"game_max_revision rises from {IntegerOf(publishedMax)} to {max}, which widens the release");
        }

        foreach (var which in (string[])["game_min", "game_max"])
        {
            var display = amended[which];
            var revision = amended[$"{which}_revision"];
            if (display is null && revision is null)
                continue;

            if (display is null || revision is null)
            {
                errors.Add($"{which} and {which}_revision have to appear together");
                continue;
            }

            var text = StringOf(display);
            if (text is null || !GameVersion.TryParse(text.Trim(), out var stated))
                errors.Add($"{which} '{Show(display)}' is not a game version string");
            else if (text.StartsWith('v'))
                errors.Add($"{which} '{text}' carries a leading v, and the game shows none");
            else if (stated.Revision != IntegerOf(revision))
                errors.Add($"{which} '{text}' is revision {stated.Revision}, and {which}_revision says {Show(revision)}");
        }

        if (amendedMin is { } low && IntegerOf(amendedMax) is { } high && high < low)
            errors.Add($"game_max_revision {high} ends up below game_min_revision {low}, so the compatibility range is empty");
    }

    private static void CheckYank(JsonObject published, JsonObject amended, List<string> errors, List<string> ownerOnly)
    {
        var yanked = amended["yanked"];
        if (yanked is not null && !IsTrue(yanked))
            errors.Add("yanked is true on a retracted release and absent otherwise");
        else if (IsTrue(published["yanked"]) && !IsTrue(yanked))
            ownerOnly.Add("the release is un-yanked, which widens the release");

        var reason = amended["yanked_reason"];
        if (reason is null)
            return;

        if (!IsTrue(yanked))
            errors.Add("yanked_reason says nothing without yanked");
        if (string.IsNullOrWhiteSpace(StringOf(reason)))
            errors.Add("yanked_reason is empty");
    }

    private static void CheckLoader(JsonObject published, JsonObject amended, List<string> errors, List<string> ownerOnly)
    {
        const string What = "the loader";
        if (amended["loader"] is not { } loader)
        {
            if (published["loader"] is not null)
                errors.Add("the loader requirement is removed, which widens the release");
            return;
        }

        if (loader is not JsonObject after)
        {
            errors.Add("loader is not an object");
            return;
        }

        Unknown(after, LoaderKeys, "loader", errors);
        if (published["loader"] is not JsonObject before)
        {
            errors.Add("a loader requirement is added where the release had none, and the amendment class covers a missing dependency entry, not a loader");
            return;
        }

        if (!JsonNode.DeepEquals(after["id"], before["id"]))
            errors.Add($"the loader changes from '{Show(before["id"])}' to '{Show(after["id"])}', which is a different requirement rather than a tighter one");
        if (!JsonNode.DeepEquals(after["source"], before["source"]))
            errors.Add("the loader's source changed, and it records where the bounds came from");

        CompareMin(before["min"], after["min"], What, errors, ownerOnly);
        CompareMax(before["max"], after["max"], What, errors, ownerOnly);
        BoundsAgree(after, What, errors);
    }

    /// <summary>A stamped bound, or null with the problem reported. A stamped bound is normalized, so a leading v says that it was written by hand.</summary>
    /// <param name="published">Whether the value is the published one, so that a value the author never touched is not reported as theirs.</param>
    private static ModVersion? Bound(JsonNode? value, string what, List<string> errors, bool published = false)
    {
        var where = published ? $"the published {what}" : what;
        if (StringOf(value) is not { } text)
        {
            errors.Add($"{where} is not a version string");
            return null;
        }

        if (!ModVersion.TryNormalizeAuthored(text.Trim(), out var normalized))
        {
            errors.Add($"{where}: version '{text}' does not parse");
            return null;
        }

        if (normalized != text)
        {
            errors.Add($"{where} '{text}' is not normalized, and a stamped bound is");
            return null;
        }

        return ModVersion.Parse(normalized);
    }

    /// <summary>A min that is lowered or removed widens the release.</summary>
    private static void CompareMin(JsonNode? published, JsonNode? amended, string what, List<string> errors, List<string> ownerOnly)
    {
        if (amended is null)
        {
            if (published is not null)
                ownerOnly.Add($"{what} removes its min '{Show(published)}', which widens the release");
            return;
        }

        if (Bound(amended, $"{what} min", errors) is not { } high || published is null)
            return;

        if (Bound(published, $"{what} min", errors, published: true) is { } low && high < low)
            ownerOnly.Add($"{what} lowers its min from '{Show(published)}' to '{Show(amended)}', which widens the release");
    }

    /// <summary>A max that is raised or removed widens the release.</summary>
    private static void CompareMax(JsonNode? published, JsonNode? amended, string what, List<string> errors, List<string> ownerOnly)
    {
        if (amended is null)
        {
            if (published is not null)
                ownerOnly.Add($"{what} removes its max '{Show(published)}', which widens the release");
            return;
        }

        if (Bound(amended, $"{what} max", errors) is not { } low || published is null)
            return;

        if (Bound(published, $"{what} max", errors, published: true) is { } high && low > high)
            ownerOnly.Add($"{what} raises its max from '{Show(published)}' to '{Show(amended)}', which widens the release");
    }

    /// <summary>A max below its own min is a range that nothing satisfies.</summary>
    private static void BoundsAgree(JsonObject entry, string what, List<string> errors)
    {
        if (entry["min"] is not { } min || entry["max"] is not { } max)
            return;

        var low = Bound(min, $"{what} min", errors);
        var high = Bound(max, $"{what} max", errors);
        if (low is { } lower && high is { } upper && upper < lower)
            errors.Add($"{what} ends up with max '{Show(max)}' below min '{Show(min)}'");
    }

    private static void CheckDependencies(JsonObject published, JsonObject amended, List<string> errors, List<string> ownerOnly)
    {
        if (amended["dependencies"] is not JsonArray amendedList)
        {
            errors.Add("dependencies is missing or is not a list");
            return;
        }

        var amendedEntries = new Dictionary<EntryKey, JsonObject>();
        var order = new List<EntryKey>();
        foreach (var node in amendedList)
        {
            if (node is not JsonObject entry)
            {
                errors.Add("a dependency entry is not an object");
                continue;
            }

            var key = EntryKey.Of(entry);
            if (!amendedEntries.TryAdd(key, entry))
            {
                errors.Add($"{key.Describe()} appears more than once");
                continue;
            }

            order.Add(key);
        }

        var publishedEntries = new Dictionary<EntryKey, JsonObject>();
        foreach (var entry in (published["dependencies"] as JsonArray ?? []).OfType<JsonObject>())
            publishedEntries[EntryKey.Of(entry)] = entry;

        var alternatives = amendedEntries.Keys.Where(key => key.AnyOf).SelectMany(key => key.Names).ToHashSet(StringComparer.Ordinal);
        foreach (var key in publishedEntries.Keys.Where(key => !amendedEntries.ContainsKey(key)).OrderBy(key => key.Sort, StringComparer.Ordinal))
        {
            var before = publishedEntries[key];
            if (StringOf(before["source"]) != "derived")
            {
                ownerOnly.Add($"{key.Describe()} is removed, which widens the release");
                errors.Add($"{key.Describe()} is removed, and the archive was not read, so nothing shows that its mod.toml does not declare it");
            }
            else if (StringOf(before["kind"]) != "optional" || key.AnyOf || !alternatives.Contains(key.Names[0]))
            {
                errors.Add($"{key.Describe()} is removed, and a derived entry stays, because the loader acts on it");
            }
        }

        foreach (var key in order.OrderBy(key => key.Sort, StringComparer.Ordinal))
        {
            var entry = amendedEntries[key];
            var what = key.Describe();
            CheckEntryShape(entry, what, errors);

            if (!publishedEntries.TryGetValue(key, out var before))
            {
                if (StringOf(entry["source"]) != "authored")
                    errors.Add($"{what} is added with source '{Show(entry["source"])}', and an added entry is authored");
                CheckAddedBounds(entry, what, errors);
                continue;
            }

            if (!JsonNode.DeepEquals(entry["kind"], before["kind"]))
                ownerOnly.Add($"{what} changes kind from '{Show(before["kind"])}' to '{Show(entry["kind"])}'");
            if (!entry.ContainsKey("any_of") && !JsonNode.DeepEquals(entry["id"], before["id"]))
                errors.Add($"{what} is renamed, and an id is not rewritten after publish");

            CheckSource(before, entry, what, errors, ownerOnly);
            CompareMin(before["min"], entry["min"], what, errors, ownerOnly);
            CompareMax(before["max"], entry["max"], what, errors, ownerOnly);
            if (entry.ContainsKey("any_of") && before.ContainsKey("any_of"))
                CheckMembers(before, entry, what, errors, ownerOnly);
        }
    }

    /// <summary>What every entry has to look like, whether it is new or was there before.</summary>
    private static void CheckEntryShape(JsonObject entry, string what, List<string> errors)
    {
        Unknown(entry, DependencyKeys, what, errors);

        var kind = StringOf(entry["kind"]);
        if (!DependencyKinds.Contains(kind))
            errors.Add($"{what} has kind '{Show(entry["kind"])}', which is not a dependency kind");

        var source = StringOf(entry["source"]);
        if (!Sources.Contains(source))
            errors.Add($"{what} has source '{Show(entry["source"])}', which is not a source");
        else if (source == "derived" && (entry["min"] is not null || entry["max"] is not null))
            errors.Add($"{what} is derived and carries a bound, which no derivation produces");

        if (entry.ContainsKey("any_of"))
        {
            if (Truthy(entry["id"]))
                errors.Add($"{what} carries both id and any_of");
            if (DependencyKinds.Contains(kind) && !AnyOfKinds.Contains(kind))
                errors.Add($"{what} carries any_of with kind '{kind}', and any_of is valid with kind required or recommends");

            if (entry["any_of"] is not JsonArray { Count: > 0 } members)
            {
                errors.Add($"{what} names no members");
                return;
            }

            foreach (var node in members)
            {
                if (node is not JsonObject member || !Truthy(member["id"]))
                {
                    errors.Add($"{what} has a member with no id");
                    continue;
                }

                var where = $"{what} member '{Show(member["id"])}'";
                Unknown(member, MemberKeys, where, errors);
                BoundsAgree(member, where, errors);
            }
        }
        else if (!Truthy(entry["id"]))
        {
            errors.Add($"{what} carries no id");
        }

        BoundsAgree(entry, what, errors);
    }

    /// <summary>An any_of set is fixed, and the bounds of each alternative may tighten.</summary>
    private static void CheckMembers(JsonObject published, JsonObject amended, string what, List<string> errors, List<string> ownerOnly)
    {
        var before = MembersOf(published);
        var after = MembersOf(amended);

        foreach (var name in before.Keys.Except(after.Keys).Order(StringComparer.Ordinal))
            errors.Add($"{what} drops the alternative '{name}', which widens the release");
        foreach (var name in after.Keys.Except(before.Keys).Order(StringComparer.Ordinal))
            errors.Add($"{what} adds the alternative '{name}', which widens the release");

        foreach (var name in before.Keys.Intersect(after.Keys).Order(StringComparer.Ordinal))
        {
            var where = $"{what} member '{name}'";
            if (!JsonNode.DeepEquals(after[name]["id"], before[name]["id"]))
                errors.Add($"{where} is renamed, and an id is not rewritten after publish");
            CompareMin(before[name]["min"], after[name]["min"], where, errors, ownerOnly);
            CompareMax(before[name]["max"], after[name]["max"], where, errors, ownerOnly);
        }
    }

    private static Dictionary<string, JsonObject> MembersOf(JsonObject entry)
    {
        var members = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var member in (entry["any_of"] as JsonArray ?? []).OfType<JsonObject>())
            members[(StringOf(member["id"]) ?? string.Empty).ToLowerInvariant()] = member;
        return members;
    }

    /// <summary>The bounds of an added entry, which no comparison parses.</summary>
    private static void CheckAddedBounds(JsonObject entry, string what, List<string> errors)
    {
        CompareMin(null, entry["min"], what, errors, errors);
        CompareMax(null, entry["max"], what, errors, errors);
        foreach (var member in (entry["any_of"] as JsonArray ?? []).OfType<JsonObject>().Where(member => Truthy(member["id"])))
        {
            var where = $"{what} member '{Show(member["id"])}'";
            CompareMin(null, member["min"], where, errors, errors);
            CompareMax(null, member["max"], where, errors, errors);
        }
    }

    /// <summary>
    /// From derived to authored records a bound or a kind that is authored onto the entry. The way back is the owner making it derived again,
    /// exactly as the archive's mod.toml declares it, which needs the archive.
    /// </summary>
    private static void CheckSource(JsonObject published, JsonObject amended, string what, List<string> errors, List<string> ownerOnly)
    {
        var before = StringOf(published["source"]);
        var after = StringOf(amended["source"]);
        if (JsonNode.DeepEquals(published["source"], amended["source"]))
        {
            if (after == "derived" && !JsonNode.DeepEquals(amended["kind"], published["kind"]))
                errors.Add($"{what} changes kind and stays derived, and a kind the archive's mod.toml does not declare is authored");
            return;
        }

        var changed = new[] { "kind", "min", "max" }.Any(key => !JsonNode.DeepEquals(amended[key], published[key]));
        if (before == "derived" && after == "authored" && changed)
            return;

        if (before == "authored" && after == "derived")
        {
            ownerOnly.Add($"{what} changes back to what the archive's mod.toml declares");
            errors.Add($"{what} turns derived, and the archive was not read to confirm it");
            return;
        }

        errors.Add($"{what} changes source from '{Show(published["source"])}' to '{Show(amended["source"])}', and only a derived entry gaining a bound or a kind does that");
    }

    /// <summary>Whether Python reads the value as true in a condition.</summary>
    private static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonObject members => members.Count > 0,
        JsonArray items => items.Count > 0,
        _ when StringOf(node) is { } text => text.Length > 0,
        _ when node.GetValueKind() == System.Text.Json.JsonValueKind.Number => node.ToJsonString().Any(digit => digit is >= '1' and <= '9'),
        _ => IsTrue(node),
    };

    /// <summary>What identifies an entry in both versions of the file. Ids compare without regard to case (RFC 0031), and an any_of entry is named by its members.</summary>
    private sealed record EntryKey(bool AnyOf, string[] Names)
    {
        public static EntryKey Of(JsonObject entry) => entry.ContainsKey("any_of")
            ? new EntryKey(true, [.. (entry["any_of"] as JsonArray ?? []).OfType<JsonObject>().Select(member => LowerId(member)).Order(StringComparer.Ordinal)])
            : new EntryKey(false, [LowerId(entry)]);

        /// <summary>The any_of entries first and then by name, the order in which the Python checks report them.</summary>
        public string Sort => (AnyOf ? "0" : "1") + string.Join('\n', Names);

        public string Describe() => AnyOf
            ? "the any_of dependency on " + (Names.Length == 0 ? "nothing" : string.Join(", ", Names))
            : $"the dependency '{Names[0]}'";

        public bool Equals(EntryKey? other) => other is not null && AnyOf == other.AnyOf && Names.SequenceEqual(other.Names, StringComparer.Ordinal);

        public override int GetHashCode() => HashCode.Combine(AnyOf, string.Join('\n', Names));

        private static string LowerId(JsonObject entry) => (StringOf(entry["id"]) ?? string.Empty).ToLowerInvariant();
    }

    [GeneratedRegex(@"^releases/([^/]+)/(.+)\.[Jj][Ss][Oo][Nn]\z")]
    private static partial Regex ReleasePath();
}
