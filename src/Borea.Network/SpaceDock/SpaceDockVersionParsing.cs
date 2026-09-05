using Borea.Core.Mods;

namespace Borea.Network.SpaceDock;

/// <summary>
/// Best-effort coercion of SpaceDock's free-text friendly_version into
/// ModVersion's strict SemVer shape. Rejection here is pure data loss (the
/// release row is skipped with no author-facing feedback), so anything that
/// plausibly names a version is coerced rather than dropped.
/// </summary>
internal static class SpaceDockVersionParsing
{
    /// <summary>
    /// Normalizes a friendly_version into a ModVersion: a leading "v" is
    /// stripped and full SemVer parses as-is. Everything else is coerced:
    /// build metadata is cut, short numeric cores such as "1.2" or "7" are
    /// padded with zeros, and a pre-release label is sanitized into the
    /// SemVer identifier charset.
    /// </summary>
    public static bool TryNormalize(string raw, out ModVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        if (ModVersion.TryParse(trimmed, out version))
            return true;

        // Build metadata is cut before the label split, because a '-' after
        // the '+' belongs to the build, not the pre-release.
        var plus = trimmed.IndexOf('+');
        if (plus >= 0)
            trimmed = trimmed[..plus];

        var core = trimmed;
        string? label = null;
        var dash = trimmed.IndexOf('-');
        if (dash >= 0)
        {
            core = trimmed[..dash];
            label = trimmed[(dash + 1)..];
        }

        if (!TryCoerceCore(core, out var major, out var minor, out var patch))
            return false;

        version = new ModVersion(major, minor, patch, label is null ? null : SanitizeLabel(label));
        return true;
    }

    private static bool TryCoerceCore(string core, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;

        var parts = core.Split('.');
        if (parts.Length >= 3)
            return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor) && int.TryParse(parts[2], out patch);

        if (parts.Length == 2)
            return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);

        return int.TryParse(parts[0], out major);
    }

    /// <summary>
    /// Coerces a free-text label into valid SemVer identifiers: characters
    /// outside the charset become hyphens, numeric identifiers lose leading
    /// zeros, and empty identifiers are dropped. Null when nothing survives.
    /// </summary>
    private static string? SanitizeLabel(string label)
    {
        var identifiers = new List<string>();

        foreach (var identifier in label.Split('.'))
        {
            var cleaned = new string(identifier.Trim()
                .Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-')
                .ToArray());

            if (cleaned.Length == 0)
                continue;

            if (cleaned.All(char.IsAsciiDigit))
            {
                cleaned = cleaned.TrimStart('0');
                if (cleaned.Length == 0)
                    cleaned = "0";
            }

            identifiers.Add(cleaned);
        }

        return identifiers.Count == 0 ? null : string.Join('.', identifiers);
    }
}
