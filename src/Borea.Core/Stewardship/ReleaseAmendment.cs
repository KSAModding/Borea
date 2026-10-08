using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Borea.Core.Game;
using Borea.Core.Mods;
using static Borea.Core.Stewardship.ReleaseJson;

namespace Borea.Core.Stewardship;

/// <summary>
/// An amendment of the release files of content-index-releases, derived as tools/amend.py derives it and checked with the
/// invariant of tools/check_amendment.py before anything is written. Every key that it does not change keeps its value and
/// its place, and a changed loader or dependency entry gets the key order of the stamper.
/// </summary>
public sealed partial class ReleaseAmendment
{
    public const string Folder = "releases";

    /// <summary>The key after which a fresh stamp writes a game bound that the file does not have yet.</summary>
    private const string AfterGameMin = "game_min_revision";

    private static readonly string[] EntryOrder = ["id", "any_of", "kind", "min", "max", "source"];
    private static readonly string[] LoaderOrder = ["id", "min", "max", "source"];

    private readonly (string Display, long Revision)? _gameMin;
    private readonly (string Display, long Revision)? _gameMax;
    private readonly bool _yank;
    private readonly string? _reason;
    private readonly string? _loaderMin;
    private readonly string? _loaderMax;
    private readonly IReadOnlyList<ReleaseDependencyAddition> _added;
    private readonly IReadOnlyList<ReleaseDependencyBounds> _bounds;
    private readonly IReadOnlyList<ReleaseDependencyKind> _kinds;
    private readonly bool _removeGameMax;
    private readonly bool _unyank;
    private readonly IReadOnlyList<string>? _os;
    private readonly bool _removeLoaderMin;
    private readonly bool _removeLoaderMax;
    private readonly IReadOnlyList<ReleaseDependencyBoundRemoval> _unbounds;

    private ReleaseAmendment(ReleaseChange change, (string, long)? gameMin, (string, long)? gameMax, string? loaderMin, string? loaderMax,
        IReadOnlyList<ReleaseDependencyAddition> added, IReadOnlyList<ReleaseDependencyBounds> bounds, IReadOnlyList<ReleaseDependencyKind> kinds,
        IReadOnlyList<string>? os, IReadOnlyList<ReleaseDependencyBoundRemoval> unbounds)
    {
        _gameMin = gameMin;
        _gameMax = gameMax;
        _yank = change.Yank;
        _reason = change.YankReason;
        _loaderMin = loaderMin;
        _loaderMax = loaderMax;
        _added = added;
        _bounds = bounds;
        _kinds = kinds;
        _removeGameMax = change.RemoveGameMax;
        _unyank = change.Unyank;
        _os = os;
        _removeLoaderMin = change.RemoveLoaderMin;
        _removeLoaderMax = change.RemoveLoaderMax;
        _unbounds = unbounds;
    }

    public static string PathOf(string id, string version)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(version);
        return $"{Folder}/{id}/{version}.json";
    }

    /// <param name="gameVersions">The versions of game-versions.json, which resolve a month to a build.</param>
    /// <param name="now">The time that decides whether a month is over.</param>
    /// <exception cref="ReleaseAmendmentRefusedException">The change names nothing, or a value does not parse or resolve.</exception>
    public static ReleaseAmendment Create(ReleaseChange change, IReadOnlyList<string> gameVersions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(gameVersions);

        var added = change.AddedDependencies.Select(addition => addition.Id.Trim() is { Length: > 0 } id && addition.Kind.Trim() is var kind
                && ReleaseAmendmentCheck.DependencyKinds.Contains(kind)
                ? new ReleaseDependencyAddition(id, kind)
                : throw Invalid($"'{addition.Id}:{addition.Kind}' is not a dependency id with one of {string.Join(", ", ReleaseAmendmentCheck.DependencyKinds)}"))
            .ToList();
        var bounds = change.DependencyBounds.Select(bound => bound.Id.Trim() is { Length: > 0 } id && (bound.Min ?? bound.Max) is not null
                ? new ReleaseDependencyBounds(id, bound.Min is null ? null : Normalize(bound.Min, id), bound.Max is null ? null : Normalize(bound.Max, id))
                : throw Invalid($"the dependency bound '{bound.Id}' names no dependency or no bound"))
            .ToList();
        var kinds = change.DependencyKinds.Select(retyped => retyped.Id.Trim() is { Length: > 0 } id && retyped.Kind.Trim() is var kind
                && ReleaseAmendmentCheck.DependencyKinds.Contains(kind)
                ? new ReleaseDependencyKind(id, kind)
                : throw Invalid($"the new kind '{retyped.Id}:{retyped.Kind}' is not a dependency id with one of {string.Join(", ", ReleaseAmendmentCheck.DependencyKinds)}"))
            .ToList();
        var unbounds = change.RemovedDependencyBounds.Select(removal => removal.Id.Trim() is { Length: > 0 } id && (removal.Min || removal.Max)
                ? new ReleaseDependencyBoundRemoval(id, removal.Min, removal.Max)
                : throw Invalid($"the bound removal '{removal.Id}' names no dependency or no bound"))
            .ToList();
        var os = change.Os?.Select(platform => platform.Trim()).ToList();
        var gameMin = Resolve(change.GameMin, "game_min", gameVersions, now);
        var gameMax = Resolve(change.GameMax, "game_max", gameVersions, now);
        var loaderMin = change.LoaderMin is null ? null : Normalize(change.LoaderMin, "loader min");
        var loaderMax = change.LoaderMax is null ? null : Normalize(change.LoaderMax, "loader max");

        if (change.YankReason is not null && !change.Yank)
            throw Invalid("a reason says nothing without a yank");
        if (change.Yank && change.Unyank)
            throw Invalid("a release is either yanked or un-yanked, not both");
        if (change.RemoveGameMax && gameMax is not null)
            throw Invalid("game_max is either set or removed, not both");
        if ((change.RemoveLoaderMin && loaderMin is not null) || (change.RemoveLoaderMax && loaderMax is not null))
            throw Invalid("a loader bound is either set or removed, not both");
        if (os is not null && (os.Any(platform => !ReleaseAmendmentCheck.OsValues.Contains(platform)) || os.Distinct().Count() != os.Count))
            throw Invalid($"os is empty or a list of distinct platforms from {string.Join(", ", ReleaseAmendmentCheck.OsValues)}");
        if (unbounds.FirstOrDefault(removal => bounds.Any(bound => ModIds.Equals(bound.Id, removal.Id)
                && ((removal.Min && bound.Min is not null) || (removal.Max && bound.Max is not null)))) is { } conflict)
            throw Invalid($"a bound of '{conflict.Id}' is either set or removed, not both");
        if (gameMin is null && gameMax is null && !change.Yank && added.Count == 0 && bounds.Count == 0 && kinds.Count == 0 && loaderMin is null && loaderMax is null
            && !change.RemoveGameMax && !change.Unyank && os is null && !change.RemoveLoaderMin && !change.RemoveLoaderMax && unbounds.Count == 0)
            throw Invalid("nothing to amend: name at least one change");

        return new ReleaseAmendment(change, gameMin, gameMax, loaderMin, loaderMax, added, bounds, kinds, os, unbounds);
    }

    /// <summary>The selected versions: those named in their order, or else the matching ones newest first.</summary>
    /// <param name="stamped">The versions of the release files of the listing, as their file names spell them.</param>
    /// <exception cref="ReleaseAmendmentRefusedException">A file name is no version, a version does not parse, or a selected version has no release file.</exception>
    public static IReadOnlyList<string> Select(IEnumerable<string> stamped, ReleaseSelection selection)
    {
        ArgumentNullException.ThrowIfNull(stamped);
        ArgumentNullException.ThrowIfNull(selection);

        var releases = stamped.Select(version => ModVersion.TryParse(version, out var precedence)
                ? (Version: version, Precedence: precedence)
                : throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NotStamperFile, $"'{version}' does not parse as SemVer 2.0.0"))
            .OrderByDescending(release => release.Precedence)
            .ThenByDescending(release => release.Version, StringComparer.Ordinal)
            .ToList();
        if (releases.Count == 0)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.UnknownRelease, "the listing has no stamped release");

        if (selection.UpToVersion is { } upTo)
        {
            var ceiling = ModVersion.Parse(Normalize(upTo, "up to"));
            var chosen = releases.Where(release => release.Precedence <= ceiling).Select(release => release.Version).ToList();
            return chosen.Count > 0 ? chosen : throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.UnknownRelease, $"no stamped release is at or below {upTo}");
        }

        if (selection.Versions is not { } versions)
            return [.. releases.Select(release => release.Version)];

        var wanted = versions.Select(version => Normalize(version, "version")).Distinct(StringComparer.Ordinal).ToList();
        var missing = wanted.Where(version => !releases.Any(release => release.Version == version)).ToList();
        return missing.Count == 0 ? wanted : throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.UnknownRelease, $"not stamped: {string.Join(", ", missing)}");
    }

    /// <summary>The amended file, or null when the release already says this.</summary>
    /// <param name="path">The path of the file, <see cref="PathOf"/> its id and version.</param>
    /// <param name="text">The text of the file on the base branch.</param>
    /// <exception cref="ReleaseAmendmentRefusedException">The file is not one that the stamper writes, or the checks of content-index-releases would reject the amended file.</exception>
    public AmendedRelease? Apply(string path, string text, ReleaseAmender amender)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        JsonNode? parsed;
        try
        {
            parsed = Parse(text);
        }
        catch (FormatException exception)
        {
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NotStamperFile, $"{path} is not readable JSON: {exception.Message}");
        }

        if (parsed is not JsonObject published)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NotStamperFile, $"{path} is not a JSON object");

        var amended = (JsonObject)published.DeepClone();
        if (!Change(amended))
            return null;

        var errors = new List<string>();
        var widened = new List<string>();
        ReleaseAmendmentCheck.Check(path, published, amended, errors, widened);
        if (amender == ReleaseAmender.Steward && widened.Count > 0)
            throw new ReleaseAmendmentRefusedException(errors.Count == 0 ? ReleaseAmendmentRefusal.Widens : ReleaseAmendmentRefusal.OutsideClass,
                [.. errors, .. widened, "only the verified owner of the listing widens a release, or a steward who names the author's request"]);
        if (errors.Count > 0)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.OutsideClass, errors);

        return new AmendedRelease(path, Write(amended), widened.Count > 0);
    }

    /// <summary>
    /// What the change does to the files when the owner makes it: it widens when it widens one file, it narrows when it changes
    /// a file without a widening, and it is refused when <see cref="Create"/> or <see cref="Apply"/> refuses it. The same checks as the preview decide it.
    /// </summary>
    public static ReleaseChangeEffect EffectOf(ReleaseChange change, IEnumerable<ReleaseFile> files, IReadOnlyList<string> gameVersions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            var amendment = Create(change, gameVersions, now);
            var effect = ReleaseChangeEffect.Unchanged;
            foreach (var file in files)
            {
                if (amendment.Apply(file.Path, file.Text, ReleaseAmender.Owner) is { } amended)
                    effect = amended.Widens || effect == ReleaseChangeEffect.Widens ? ReleaseChangeEffect.Widens : ReleaseChangeEffect.Narrows;
            }

            return effect;
        }
        catch (ReleaseAmendmentRefusedException)
        {
            return ReleaseChangeEffect.Refused;
        }
    }

    /// <summary>Every requested change, in the order of tools/amend.py. Returns whether the file moved.</summary>
    private bool Change(JsonObject document)
    {
        var changed = false;
        if (_gameMin is { } gameMin)
            changed |= SetGameBound(document, "game_min", gameMin);
        if (_gameMax is { } gameMax)
            changed |= SetGameBound(document, "game_max", gameMax);
        if (_removeGameMax)
            changed |= RemoveKeys(document, "game_max", "game_max_revision");
        if (_os is not null)
            changed |= SetOs(document, _os);
        if (_yank)
            changed |= SetYank(document);
        if (_unyank)
            changed |= RemoveKeys(document, "yanked", "yanked_reason");
        foreach (var addition in _added)
            changed |= AddDependency(document, addition);
        foreach (var bounds in _bounds)
            changed |= SetDependencyBounds(document, bounds);
        foreach (var removal in _unbounds)
            changed |= RemoveDependencyBounds(document, removal);
        foreach (var kind in _kinds)
            changed |= SetDependencyKind(document, kind);
        if (_loaderMin is not null || _loaderMax is not null || _removeLoaderMin || _removeLoaderMax)
            changed |= SetLoaderBounds(document);
        return changed;
    }

    /// <summary>Removes the keys that the file has. Every other key keeps its place.</summary>
    private static bool RemoveKeys(JsonObject document, params ReadOnlySpan<string> keys)
    {
        var changed = false;
        foreach (var key in keys)
            changed |= document.Remove(key);
        return changed;
    }

    /// <summary>Writes the platforms where a fresh stamp puts them, after the game bounds, or removes os for an empty list.</summary>
    private static bool SetOs(JsonObject document, IReadOnlyList<string> platforms)
    {
        if (platforms.Count == 0)
            return document.Remove("os");

        var value = new JsonArray([.. platforms.Select(platform => (JsonNode?)JsonValue.Create(platform))]);
        if (JsonNode.DeepEquals(document["os"], value))
            return false;

        if (document.ContainsKey("os"))
        {
            document["os"] = value;
            return true;
        }

        var after = document.ContainsKey("game_max_revision") ? "game_max_revision" : AfterGameMin;
        if (!document.ContainsKey(after))
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NotStamperFile,
                $"the release file carries no {after}, so it was not written by the stamper and there is nowhere to put os");

        InsertAfter(document, after, [KeyValuePair.Create("os", (JsonNode?)value)]);
        return true;
    }

    /// <summary>Writes a resolved game bound where a fresh stamp puts it.</summary>
    private static bool SetGameBound(JsonObject document, string which, (string Display, long Revision) bound)
    {
        var revisionKey = $"{which}_revision";
        if (StringOf(document[which]) == bound.Display && IntegerOf(document[revisionKey]) == bound.Revision)
            return false;

        if (document.ContainsKey(which))
        {
            document[which] = bound.Display;
            document[revisionKey] = bound.Revision;
            return true;
        }

        if (!document.ContainsKey(AfterGameMin))
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NotStamperFile,
                $"the release file carries no {AfterGameMin}, so it was not written by the stamper and there is nowhere to put a game bound");

        InsertAfter(document, AfterGameMin, [KeyValuePair.Create(which, (JsonNode?)bound.Display), KeyValuePair.Create(revisionKey, (JsonNode?)bound.Revision)]);
        return true;
    }

    /// <summary>Puts new keys right after <paramref name="after"/>. A key that follows keeps its place and its value, as a Python dict does when the loop sets it again.</summary>
    private static void InsertAfter(JsonObject document, string after, IReadOnlyList<KeyValuePair<string, JsonNode?>> inserted)
    {
        var members = document.Select(member => KeyValuePair.Create(member.Key, member.Value?.DeepClone())).ToList();
        document.Clear();
        foreach (var (key, value) in members)
        {
            document[key] = value;
            if (key == after)
            {
                foreach (var (newKey, newValue) in inserted)
                    document[newKey] = newValue;
            }
        }
    }

    /// <summary>Retracts one build. A new yanked key goes last, as RFC 0031 lists it.</summary>
    private bool SetYank(JsonObject document)
    {
        var changed = !IsTrue(document["yanked"]);
        document["yanked"] = true;
        if (_reason is not null && StringOf(document["yanked_reason"]) != _reason)
        {
            document["yanked_reason"] = _reason;
            changed = true;
        }

        return changed;
    }

    private bool SetLoaderBounds(JsonObject document)
    {
        var loader = document["loader"] switch
        {
            null => throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NoLoader, "the release states no loader, and adding one is not an amendment"),
            JsonObject entry => entry,
            _ => throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.OutsideClass, "loader is not an object"),
        };

        var updated = WithBounds(loader, _loaderMin, _loaderMax);
        var changed = updated is not null;
        updated ??= (JsonObject)loader.DeepClone();
        changed |= RemoveKeys(updated, [.. Removed(_removeLoaderMin, _removeLoaderMax)]);
        if (!changed)
            return false;

        document["loader"] = Reorder(updated, LoaderOrder);
        return true;
    }

    /// <summary>The bound keys that a removal names.</summary>
    private static IEnumerable<string> Removed(bool min, bool max)
    {
        if (min)
            yield return "min";
        if (max)
            yield return "max";
    }

    /// <summary>An entry that was missing, which RFC 0031 admits, a conflict included.</summary>
    private static bool AddDependency(JsonObject document, ReleaseDependencyAddition addition)
    {
        var dependencies = DependenciesOf(document);
        if (IndexOf(dependencies, addition.Id) >= 0)
            return false;

        dependencies.Add(new JsonObject { ["id"] = addition.Id, ["kind"] = addition.Kind, ["source"] = "authored" });
        document["dependencies"] = dependencies;
        return true;
    }

    private static bool SetDependencyBounds(JsonObject document, ReleaseDependencyBounds bounds)
    {
        var dependencies = DependenciesOf(document);
        var index = IndexOf(dependencies, bounds.Id);
        if (index < 0)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NoDependency,
                $"the release states no dependency on '{bounds.Id}'. An entry that was missing is added as a new dependency, and an any_of entry is edited by hand");

        if (WithBounds((JsonObject)dependencies[index]!, bounds.Min, bounds.Max) is not { } updated)
            return false;

        // Once the author adds the matching authored entry, the next stamp writes exactly this, so it stops being derived now.
        if (StringOf(updated["source"]) == "derived")
            updated["source"] = "authored";

        dependencies[index] = Reorder(updated, EntryOrder);
        document["dependencies"] = dependencies;
        return true;
    }

    /// <summary>A stated entry without the named bounds. A derived entry becomes authored as with a new bound.</summary>
    private static bool RemoveDependencyBounds(JsonObject document, ReleaseDependencyBoundRemoval removal)
    {
        var dependencies = DependenciesOf(document);
        var index = IndexOf(dependencies, removal.Id);
        if (index < 0)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NoDependency,
                $"the release states no dependency on '{removal.Id}'. An entry that was missing is added as a new dependency, and an any_of entry is edited by hand");

        var entry = (JsonObject)dependencies[index]!.DeepClone();
        if (!RemoveKeys(entry, [.. Removed(removal.Min, removal.Max)]))
            return false;

        if (StringOf(entry["source"]) == "derived")
            entry["source"] = "authored";

        dependencies[index] = Reorder(entry, EntryOrder);
        document["dependencies"] = dependencies;
        return true;
    }

    /// <summary>
    /// A new kind of a stated entry. A derived entry becomes authored as with a bound, so it stays in the release with the new kind,
    /// because the stamp keeps an authored entry in place of the one that the archive's mod.toml declares.
    /// </summary>
    private static bool SetDependencyKind(JsonObject document, ReleaseDependencyKind change)
    {
        var dependencies = DependenciesOf(document);
        var index = IndexOf(dependencies, change.Id);
        if (index < 0)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NoDependency,
                $"the release states no dependency on '{change.Id}'. An entry that was missing is added as a new dependency, and an any_of entry is edited by hand");

        var entry = (JsonObject)dependencies[index]!.DeepClone();
        if (StringOf(entry["kind"]) == change.Kind)
            return false;

        entry["kind"] = change.Kind;
        if (StringOf(entry["source"]) == "derived")
            entry["source"] = "authored";

        dependencies[index] = Reorder(entry, EntryOrder);
        document["dependencies"] = dependencies;
        return true;
    }

    /// <summary>A copy of the entry with the bounds, or null when it already has them.</summary>
    private static JsonObject? WithBounds(JsonObject entry, string? min, string? max)
    {
        var updated = (JsonObject)entry.DeepClone();
        var changed = false;
        foreach (var (key, value) in (ReadOnlySpan<(string, string?)>)[("min", min), ("max", max)])
        {
            if (value is not null && StringOf(updated[key]) != value)
            {
                updated[key] = value;
                changed = true;
            }
        }

        return changed ? updated : null;
    }

    /// <summary>A copy of the dependency list, empty when the file has none.</summary>
    private static JsonArray DependenciesOf(JsonObject document) => document["dependencies"] switch
    {
        null => [],
        JsonArray dependencies => (JsonArray)dependencies.DeepClone(),
        _ => throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.OutsideClass, "dependencies is missing or is not a list"),
    };

    private static int IndexOf(JsonArray dependencies, string id)
    {
        for (var index = 0; index < dependencies.Count; index++)
        {
            if (dependencies[index] is JsonObject entry && ModIds.Equals(StringOf(entry["id"]) ?? string.Empty, id))
                return index;
        }

        return -1;
    }

    /// <summary>The keys of the entry in the order that the stamper writes them, and any other key after them.</summary>
    private static JsonObject Reorder(JsonObject entry, string[] order)
    {
        var ordered = new JsonObject();
        foreach (var key in order.Where(entry.ContainsKey))
            ordered[key] = entry[key]?.DeepClone();
        foreach (var (key, value) in entry.Where(member => !ordered.ContainsKey(member.Key)))
            ordered[key] = value?.DeepClone();
        return ordered;
    }

    /// <summary>
    /// A game bound as (display, revision), resolved against game-versions.json as the stamper resolves it: a build carries its own revision,
    /// and a month gives its first build as a lower bound and its last build as an upper bound once the month is over.
    /// </summary>
    private static (string, long)? Resolve(string? value, string which, IReadOnlyList<string> gameVersions, DateTimeOffset now)
    {
        if (value is null)
            return null;

        value = value.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..].Trim();
        if (value.Length == 0)
            throw Invalid($"{which} is empty");

        if (GameVersion.TryParse(value, out var build))
            return (value, build.Revision);

        var match = MonthPattern().Match(value);
        if (!match.Success)
            throw Invalid($"{which} '{value}' is neither a game version nor a month");

        var month = (Year: int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), Month: int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        var today = now.UtcDateTime;
        if (which == "game_max" && (today.Year, today.Month).CompareTo(month) <= 0)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.MonthNotOver,
                $"{which} '{value}' names a month that is not over, so it has no last revision yet. Name a build instead");

        var builds = gameVersions
            .Select(version => GameVersion.TryParse(version.Trim(), out var parsed) ? (Display: version, parsed.Year, parsed.Month, parsed.Revision) : default)
            .Where(candidate => candidate.Display is not null && (candidate.Year, candidate.Month) == month)
            .ToList();
        if (builds.Count == 0)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.UnknownMonth, $"{which} '{value}' names a month with no build in the game release list");

        var revision = which == "game_min" ? builds.Min(candidate => candidate.Revision) : builds.Max(candidate => candidate.Revision);
        return (builds.First(candidate => candidate.Revision == revision).Display, revision);
    }

    /// <summary>A version bound as the index stores it.</summary>
    private static string Normalize(string version, string what) =>
        ModVersion.TryNormalizeAuthored(version.Trim(), out var normalized)
            ? normalized
            : throw Invalid($"{what}: version '{version}' does not parse");

    private static ReleaseAmendmentRefusedException Invalid(string detail) => new(ReleaseAmendmentRefusal.InvalidChange, detail);

    /// <summary>A month as YEAR.MONTH in ASCII digits only, because int.Parse reads no other digits.</summary>
    [GeneratedRegex(@"^([0-9]{4})\.([0-9]{1,2})$")]
    private static partial Regex MonthPattern();
}
