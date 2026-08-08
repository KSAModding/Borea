using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// One member of an any_of dependency entry: a mod id with its own optional version bounds.
/// </summary>
public sealed class ModDependencyAlternative
{
    /// <summary>
    /// The ID of the alternative mod.
    /// </summary>
    public string ModId { get; }

    /// <summary>
    /// Minimum version, inclusive, if any.
    /// </summary>
    public ModVersion? MinVersion { get; }

    /// <summary>
    /// Maximum version, inclusive. Absent means open.
    /// </summary>
    public ModVersion? MaxVersion { get; }

    public ModDependencyAlternative(string modId, ModVersion? minVersion = null, ModVersion? maxVersion = null)
    {
        ModIds.Validate(modId, nameof(modId));

        if (minVersion is { } min && maxVersion is { } max && max.CompareTo(min) < 0)
            throw new ArgumentOutOfRangeException(nameof(maxVersion), "The maximum version cannot be below the minimum.");

        ModId = modId;
        MinVersion = minVersion;
        MaxVersion = maxVersion;
    }

    /// <summary>
    /// Whether the version lies within the inclusive bounds. Absent bounds are open.
    /// </summary>
    public bool BoundsContain(ModVersion version)
    {
        if (MinVersion is { } min && version.CompareTo(min) < 0)
            return false;

        if (MaxVersion is { } max && version.CompareTo(max) > 0)
            return false;

        return true;
    }
}
