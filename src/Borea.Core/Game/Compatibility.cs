using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Game;

/// <summary>
/// Classifies content against the installed game and the target platform.
/// </summary>
public static class Compatibility
{
    /// <summary>
    /// The state the revision bounds put the installed game in (RFC 0017).
    /// </summary>
    public static GameCompatibility Evaluate(int? minRevision, int? maxRevision, GameVersion? installed)
    {
        if (installed is not { } version || minRevision is not { } min)
            return GameCompatibility.Unknown;

        if (version.Revision < min)
            return GameCompatibility.Incompatible;

        return maxRevision is { } max && version.Revision > max
            ? GameCompatibility.Untested
            : GameCompatibility.Compatible;
    }

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
    /// The state the authored bounds of a pack version put the installed game in. A pack
    /// document is not stamped, so a bound can still be a month such as "2026.7", which
    /// gives an unknown state. The lower bound is compared on its own, so a month in the
    /// upper bound does not hide an incompatible game.
    /// </summary>
    public static GameCompatibility Evaluate(ModPackMetadata pack, GameVersion? installed)
    {
        ArgumentNullException.ThrowIfNull(pack);

        if (installed is not { } game || !GameVersion.TryParse(pack.GameMin, out var min))
            return GameCompatibility.Unknown;

        if (game.Revision < min.Revision)
            return GameCompatibility.Incompatible;

        int? maxRevision = null;
        if (pack.GameMax is not null)
        {
            if (!GameVersion.TryParse(pack.GameMax, out var max))
                return GameCompatibility.Unknown;
            maxRevision = max.Revision;
        }

        return Evaluate(min.Revision, maxRevision, game);
    }

    /// <summary>
    /// The same check for the authored bounds of a pack version, where a month bound such as "2026.7" matches no build.
    /// </summary>
    public static bool SupportsAnyBuild(ModPackMetadata pack, int? fromRevision, int? toRevision)
    {
        ArgumentNullException.ThrowIfNull(pack);

        if (!GameVersion.TryParse(pack.GameMin, out var min))
            return false;

        int? maxRevision = null;
        if (pack.GameMax is not null)
        {
            if (!GameVersion.TryParse(pack.GameMax, out var max))
                return false;
            maxRevision = max.Revision;
        }

        return SupportsAnyBuild(min.Revision, maxRevision, fromRevision, toRevision);
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

    private static OsPlatform? Parse(string value) => value.ToLowerInvariant() switch
    {
        "windows" => OsPlatform.Windows,
        "linux" => OsPlatform.Linux,
        "macos" => OsPlatform.MacOs,
        _ => null,
    };
}
