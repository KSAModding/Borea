using Borea.Core.Mods;

namespace Borea.Network.SpaceDock;

/// <summary>
/// Best-effort coercion of SpaceDock's free-text friendly_version into
/// ModVersion's strict SemVer shape.
/// </summary>
internal static class SpaceDockVersionParsing
{
    /// <summary>
    /// Normalizes a friendly_version into a ModVersion: a leading "v" is
    /// stripped, full SemVer parses as-is, short numeric forms such as "1.2"
    /// or "7" are padded with zeros.
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

        // Carry a pre-release label through the padding branches, so
        // "1.2-beta" does not pose as a stable release.
        var core = trimmed;
        var suffix = string.Empty;
        var dash = trimmed.IndexOf('-');
        if (dash >= 0)
        {
            core = trimmed[..dash];
            suffix = trimmed[dash..];
        }

        var parts = core.Split('.');
        if (parts.Length >= 3 && parts.Take(3).All(p => int.TryParse(p, out _)))
            return ModVersion.TryParse($"{parts[0]}.{parts[1]}.{parts[2]}{suffix}", out version);

        if (parts.Length == 2 && parts.All(p => int.TryParse(p, out _)))
            return ModVersion.TryParse($"{parts[0]}.{parts[1]}.0{suffix}", out version);

        if (parts.Length == 1 && int.TryParse(parts[0], out _))
            return ModVersion.TryParse($"{parts[0]}.0.0{suffix}", out version);

        return false;
    }
}
