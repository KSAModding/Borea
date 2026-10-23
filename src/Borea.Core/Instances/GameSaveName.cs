using System.Text;

namespace Borea.Core.Instances;

/// <summary>
/// The rules of the game's SaveName.Sanitize, which the game applies to a
/// name before it writes a save or a vehicle under it.
/// </summary>
public static class GameSaveName
{
    public const int MaxLength = 64;

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// The name the game would keep: only letters, digits, single spaces, "-"
    /// and "_", no space at either end, and a "_" after a Windows device name.
    /// Empty when nothing is left.
    /// </summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var kept = new StringBuilder(MaxLength);
        foreach (var c in name)
        {
            if (kept.Length == MaxLength)
                break;

            var isLeadingOrRepeatedSpace = c == ' ' && (kept.Length == 0 || kept[^1] == ' ');
            if (IsAllowed(c) && !isLeadingOrRepeatedSpace)
                kept.Append(c);
        }

        while (kept.Length > 0 && kept[^1] == ' ')
            kept.Length--;

        var sanitized = kept.ToString();
        return sanitized.Length > 0 && ReservedDeviceNames.Contains(sanitized, StringComparer.OrdinalIgnoreCase) ? sanitized + "_" : sanitized;
    }

    /// <summary>True when the game keeps the name as it is.</summary>
    public static bool IsValid(string? name) => !string.IsNullOrEmpty(name) && string.Equals(name, Sanitize(name), StringComparison.Ordinal);

    private static bool IsAllowed(char c) => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_';
}
