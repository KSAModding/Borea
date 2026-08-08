using System.Text.RegularExpressions;

namespace Borea.Core.Mods;

/// <summary>
/// The content id rules: ids compare case-insensitively, and a valid id is a
/// folder name that works on Windows, Linux, and macOS at once.
/// </summary>
public static partial class ModIds
{
    /// <summary>
    /// The comparer every id comparison and id-keyed collection goes through.
    /// </summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    private static readonly string[] ReservedNames =
    {
        "Core",
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Whether two ids name the same content.
    /// </summary>
    public static bool Equals(string? left, string? right) => Comparer.Equals(left, right);

    /// <summary>
    /// Whether the id satisfies the format rules, including the reserved names.
    /// </summary>
    public static bool IsValid(string? id)
    {
        if (id is null || !Pattern().IsMatch(id))
            return false;

        var stem = id.Split('.')[0];
        return !ReservedNames.Any(r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Throws when the id does not satisfy the format rules.
    /// </summary>
    public static void Validate(string id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id cannot be null or whitespace.", paramName);

        if (!IsValid(id))
            throw new ArgumentException($"'{id}' is not a valid content id.", paramName);
    }

    // 1-64 chars, ASCII letters, digits, '-', '_', '.', first and last a letter or digit.
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$")]
    private static partial Regex Pattern();
}
