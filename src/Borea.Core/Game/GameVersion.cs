using System.Globalization;
using System.Text.RegularExpressions;

namespace Borea.Core.Game;

/// <summary>
/// KSA's version scheme: Year.Month.BuildNumber.Revision, plus an optional -suffix and +hash.
/// Only the revision orders releases; the build number is machine-local and year and month are display only.
/// Equality compares every component, so versions with equal revisions can still be distinct.
/// See https://github.com/KSAModding/mod-manager-design/blob/main/rfcs/0017-game-version-ordering-and-compatibility.md
/// </summary>
public readonly partial record struct GameVersion : IComparable<GameVersion>
{
    public int Year { get; }
    public int Month { get; }
    public int BuildNumber { get; }
    public int Revision { get; }
    public string Suffix { get; }

    public GameVersion(int year, int month, int buildNumber, int revision, string suffix = "")
    {
        if (year < 0)
            throw new ArgumentOutOfRangeException(nameof(year), "Year cannot be negative.");

        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

        if (buildNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(buildNumber), "Build number cannot be negative.");

        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision), "Revision cannot be negative.");

        Year = year;
        Month = month;
        BuildNumber = buildNumber;
        Revision = revision;
        Suffix = string.IsNullOrEmpty(suffix) ? string.Empty
            : suffix.StartsWith('-') ? suffix
            : "-" + suffix;
    }

    public static GameVersion Parse(string value)
    {
        if (!TryParse(value, out var result))
            throw new FormatException($"'{value}' is not a valid game version.");

        return result;
    }

    public static bool TryParse(string? value, out GameVersion result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = Pattern().Match(value);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["Year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(match.Groups["Month"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(match.Groups["Build"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var build) ||
            !int.TryParse(match.Groups["Revision"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
            return false;

        if (month is < 1 or > 12)
            return false;

        // The constructor normalizes the leading dash, like VersionInfo.ParseVersion.
        var suffix = match.Groups["Suffix"].Success ? match.Groups["Suffix"].Value : string.Empty;

        result = new GameVersion(year, month, build, revision, suffix);
        return true;
    }

    /// <summary>
    /// Newer means a higher revision, nothing else is compared.
    /// </summary>
    public int CompareTo(GameVersion other) => Revision.CompareTo(other.Revision);

    public static bool operator <(GameVersion left, GameVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(GameVersion left, GameVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(GameVersion left, GameVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(GameVersion left, GameVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Year}.{Month}.{BuildNumber}.{Revision}{Suffix}";

    // The game's own version pattern (VersionInfo.MyRegex).
    [GeneratedRegex(@"^v?(?<Year>\d+)\.(?<Month>\d+)\.(?<Build>\d+)\.(?<Revision>\d+)(?:-(?<Suffix>[^+]+))?(?:\+.*)?$")]
    private static partial Regex Pattern();
}
