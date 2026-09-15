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
