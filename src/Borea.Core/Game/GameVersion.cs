namespace Borea.Core.Game;

/// <summary>
/// KSA's own versioning scheme: Year.Month.BuildNumber.LastCommitNumber.
/// Currently Opaque and only compatability checks are equality checks.
/// </summary>
public readonly record struct GameVersion
{
    public int Year { get; }
    public int Month { get; }
    public int BuildNumber { get; }
    public int LastCommitNumber { get; }

    public GameVersion(int year, int month, int buildNumber, int lastCommitNumber)
    {
        if (year < 0)
            throw new ArgumentOutOfRangeException(nameof(year), "Year cannot be negative.");

        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

        if (buildNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(buildNumber), "Build number cannot be negative.");

        if (lastCommitNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(lastCommitNumber), "Last commit number cannot be negative.");

        Year = year;
        Month = month;
        BuildNumber = buildNumber;
        LastCommitNumber = lastCommitNumber;
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

        var parts = value.Split('.');
        if (parts.Length != 4)
            return false;

        if (!int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var build) ||
            !int.TryParse(parts[3], out var commit))
            return false;

        if (year < 0 || month is < 1 or > 12 || build < 0 || commit < 0)
            return false;

        result = new GameVersion(year, month, build, commit);
        return true;
    }

    public override string ToString() => $"{Year}.{Month}.{BuildNumber}.{LastCommitNumber}";
}