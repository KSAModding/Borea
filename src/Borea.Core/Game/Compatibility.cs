using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Game;

/// <summary>
/// Classifies content against the installed game and the target platform.
/// </summary>
public static class Compatibility
{
    /// <summary>
    /// The state the revision bounds put the installed game in (RFC 0017, amended by RFC 0091).
    /// A game below the lower bound or above a stated upper bound is incompatible, and an absent upper bound is open.
    /// </summary>
    public static GameCompatibility Evaluate(int? minRevision, int? maxRevision, GameVersion? installed)
    {
        if (installed is not { } version || minRevision is not { } min)
            return GameCompatibility.Unknown;

        return version.Revision < min || IsAbove(maxRevision, version)
            ? GameCompatibility.Incompatible
            : GameCompatibility.Compatible;
    }

    /// <summary>
    /// True when the release fits the installed game only by its upper bound, so the game is newer than its game_max and not older than its game_min.
    /// </summary>
    public static bool IsAboveUpperBound(ModVersionMetadata release, GameVersion? installed)
    {
        ArgumentNullException.ThrowIfNull(release);

        return installed is { } game && game.Revision >= release.GameMinRevision && IsAbove(release.GameMaxRevision, game);
    }

    /// <summary>
    /// The same check for the authored bounds of a pack version, with a month bound resolved as <see cref="Evaluate(ModPackMetadata, GameVersion?, GameReleaseList)"/> resolves it.
    /// </summary>
    public static bool IsAboveUpperBound(ModPackMetadata pack, GameVersion? installed, GameReleaseList releases)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(releases);

        if (installed is not { } game || pack.GameMax is null || Evaluate(pack, installed, releases) != GameCompatibility.Incompatible)
            return false;

        return releases.WithBuild(game).TryResolveUpperBound(pack.GameMax, out var max) && IsAbove(max, game);
    }

    private static bool IsAbove(int? maxRevision, GameVersion installed) => maxRevision is { } max && installed.Revision > max;

    /// <summary>
    /// Whether the bounds support at least one build from <paramref name="fromRevision"/> to <paramref name="toRevision"/>, both included, where an absent bound is open.
    /// </summary>
    public static bool SupportsAnyBuild(int minRevision, int? maxRevision, int? fromRevision, int? toRevision)
        => (toRevision is not { } last || minRevision <= last)
            && (maxRevision is not { } max || fromRevision is not { } first || first <= max);

    /// <summary>
    /// The state the bounds of a stamped release put the installed game in.
    /// A release always carries a lower bound.
    /// </summary>
    public static GameCompatibility Evaluate(ModVersionMetadata release, GameVersion? installed)
    {
        if (release is null)
            throw new ArgumentNullException(nameof(release));

        return Evaluate(release.GameMinRevision, release.GameMaxRevision, installed);
    }

    /// <summary>
    /// The state the authored bounds of a pack version put the installed game in, with a month bound resolved through <paramref name="releases"/> and the installed build.
    /// </summary>
    public static GameCompatibility Evaluate(ModPackMetadata pack, GameVersion? installed, GameReleaseList releases)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(releases);

        if (installed is not { } game)
            return GameCompatibility.Unknown;

        var known = releases.WithBuild(game);
        if (!known.TryResolveLowerBound(pack.GameMin, out var min))
            return GameCompatibility.Unknown;

        if (game.Revision < min)
            return GameCompatibility.Incompatible;

        int? max = null;
        if (pack.GameMax is not null && !known.TryResolveUpperBound(pack.GameMax, out max))
            return GameCompatibility.Unknown;

        return Evaluate(min, max, game);
    }

    /// <summary>
    /// The state a pack version and the releases it pins put the installed game in.
    /// The least compatible one decides, so a pin below its game_min or above its game_max makes the pack incompatible (RFC 0091).
    /// </summary>
    public static GameCompatibility Evaluate(ModPackMetadata pack, IEnumerable<ModVersionMetadata> pinned, GameVersion? installed, GameReleaseList releases)
    {
        ArgumentNullException.ThrowIfNull(pinned);

        var least = Evaluate(pack, installed, releases);
        foreach (var release in pinned)
            least = Least(least, Evaluate(release, installed));

        return least;
    }

    /// <summary>
    /// The same check for a pack version, where a build counts only when the authored bounds of the pack and the bounds of every release in <paramref name="pinned"/> admit it.
    /// A month bound resolves through <paramref name="releases"/>, and a bound that does not resolve supports no build.
    /// </summary>
    public static bool SupportsAnyBuild(ModPackMetadata pack, IEnumerable<ModVersionMetadata> pinned, int? fromRevision, int? toRevision, GameReleaseList releases)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(releases);

        if (!releases.TryResolveLowerBound(pack.GameMin, out var min))
            return false;

        int? max = null;
        if (pack.GameMax is not null && !releases.TryResolveUpperBound(pack.GameMax, out max))
            return false;

        foreach (var release in pinned)
        {
            min = Math.Max(min, release.GameMinRevision);
            if (release.GameMaxRevision is { } releaseMax)
                max = max is { } current ? Math.Min(current, releaseMax) : releaseMax;
        }

        return (max is not { } last || min <= last) && SupportsAnyBuild(min, max, fromRevision, toRevision);
    }

    /// <summary>
    /// What the os list says about the target platform.
    /// </summary>
    public static OsSupport EvaluateOs(IReadOnlyList<string>? os, OsPlatform target)
    {
        if (os is null || os.Count == 0)
            return new OsSupport(isSupported: true);

        var unrecognized = new List<string>();
        var supported = false;

        foreach (var entry in os)
        {
            var platform = Parse(entry);
            if (platform is null)
                unrecognized.Add(entry);
            else if (platform == target)
                supported = true;
        }

        return new OsSupport(supported, unrecognized);
    }

    /// <summary>Incompatible blocks, so it ranks above Unknown, which only warns.</summary>
    private static GameCompatibility Least(GameCompatibility first, GameCompatibility second)
        => Rank(first) >= Rank(second) ? first : second;

    private static int Rank(GameCompatibility compatibility) => compatibility switch
    {
        GameCompatibility.Compatible => 0,
        GameCompatibility.Unknown => 1,
        _ => 2,
    };

    private static OsPlatform? Parse(string value) => value.ToLowerInvariant() switch
    {
        "windows" => OsPlatform.Windows,
        "linux" => OsPlatform.Linux,
        "macos" => OsPlatform.MacOs,
        _ => null,
    };
}
