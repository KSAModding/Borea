using Borea.Core.Mods;

namespace Borea.Network.SpaceDock;

/// <summary>
/// Best-effort coercion of SpaceDock's free-text friendly_version into
/// ModVersion's strict Major.Minor.Patch shape.
/// </summary>
internal static class SpaceDockVersionParsing
{
    /// <summary>
    /// Attempts to normalize SpaceDock's free-text friendly_version into ModVersion's strict Major.Minor.Patch shape.
    /// </summary>
    public static bool TryNormalize(string raw, out ModVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        var parts = trimmed.Split('.');
        if (parts.Length >= 3 && parts.Take(3).All(p => int.TryParse(p, out _)))
            return ModVersion.TryParse($"{parts[0]}.{parts[1]}.{parts[2]}", out version);

        if (parts.Length == 2 && parts.All(p => int.TryParse(p, out _)))
            return ModVersion.TryParse($"{parts[0]}.{parts[1]}.0", out version);

        if (parts.Length == 1 && int.TryParse(parts[0], out _))
            return ModVersion.TryParse($"{parts[0]}.0.0", out version);

        return false;
    }
}
