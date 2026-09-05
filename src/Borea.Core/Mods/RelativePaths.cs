namespace Borea.Core.Mods;

/// <summary>
/// The path rule every install descriptor shares (RFC 0035 rules 1 and 2).
/// </summary>
public static class RelativePaths
{
    /// <summary>
    /// A relative, '/' separated path that stays inside its anchor. Null passes
    /// through, empty does not.
    /// </summary>
    public static string? Contained(string? value, string paramName)
    {
        if (value is null)
            return null;

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A stated path cannot be empty, leave it absent instead.", paramName);

        if (value.StartsWith('/') || value.StartsWith('~') || value.Contains('\\') ||
            value.Contains(':') || value.Split('/').Any(segment => segment == ".."))
        {
            throw new ArgumentException(
                "A path must be relative, '/' separated, and must stay inside its anchor.", paramName);
        }

        return value;
    }
}
