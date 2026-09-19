using System.Globalization;
using System.Text.RegularExpressions;
using Borea.Core.Index;

namespace Borea.Core.Game;

/// <summary>
/// The known game builds, which resolve an authored bound such as "2026.7" to a revision (RFC 0017).
/// </summary>
public sealed partial class GameReleaseList
{
    private readonly GameVersion[] _versions;

    public static GameReleaseList Empty { get; } = new(Array.Empty<GameVersion>());

    /// <param name="versions">An entry that is not a full game version is skipped.</param>
    public GameReleaseList(IEnumerable<string> versions)
        : this(Parse(versions))
    {
    }

    private GameReleaseList(GameVersion[] versions) => _versions = versions;

    public static GameReleaseList From(ContentIndexGameVersions? gameVersions) =>
        gameVersions is null ? Empty : new GameReleaseList(gameVersions.Versions);

    public bool IsEmpty => _versions.Length == 0;

    public GameReleaseList WithBuild(GameVersion build) => new([.. _versions, build]);

    /// <summary>The builds above <paramref name="revision"/>, one per revision, newest first.</summary>
    public IReadOnlyList<GameVersion> NewerThan(int revision) => _versions
        .Where(version => version.Revision > revision)
        .DistinctBy(version => version.Revision)
        .OrderByDescending(version => version.Revision)
        .ToList();

    /// <summary>A month gives the first revision of that month.</summary>
    public bool TryResolveLowerBound(string? bound, out int revision)
    {
        revision = 0;
        if (GameVersion.TryParse(bound, out var version))
        {
            revision = version.Revision;
            return true;
        }

        if (!TryParseMonth(bound, out var year, out var month) || !TryGetMonthRevisions(year, month, out var first, out _))
            return false;

        revision = first;
        return true;
    }

    /// <summary>A month gives its last revision once the list has a build of a later month, and until then null, which is an open bound.</summary>
    public bool TryResolveUpperBound(string? bound, out int? revision)
    {
        revision = null;
        if (GameVersion.TryParse(bound, out var version))
        {
            revision = version.Revision;
            return true;
        }

        if (!TryParseMonth(bound, out var year, out var month) || !TryGetMonthRevisions(year, month, out _, out var last))
            return false;

        if (_versions.Any(candidate => (candidate.Year, candidate.Month).CompareTo((year, month)) > 0))
            revision = last;
        return true;
    }

    private static GameVersion[] Parse(IEnumerable<string> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        return versions
            .Select(value => GameVersion.TryParse(value, out var version) ? version : (GameVersion?)null)
            .OfType<GameVersion>()
            .ToArray();
    }

    private bool TryGetMonthRevisions(int year, int month, out int first, out int last)
    {
        first = int.MaxValue;
        last = int.MinValue;
        foreach (var version in _versions.Where(candidate => candidate.Year == year && candidate.Month == month))
        {
            first = Math.Min(first, version.Revision);
            last = Math.Max(last, version.Revision);
        }

        return last >= first;
    }

    private static bool TryParseMonth(string? value, out int year, out int month)
    {
        year = 0;
        month = 0;
        var match = value is null ? Match.Empty : MonthPattern().Match(value);
        return match.Success
            && int.TryParse(match.Groups["Year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(match.Groups["Month"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && month is >= 1 and <= 12;
    }

    [GeneratedRegex(@"^(?<Year>\d{4})\.(?<Month>\d{1,2})$")]
    private static partial Regex MonthPattern();
}
