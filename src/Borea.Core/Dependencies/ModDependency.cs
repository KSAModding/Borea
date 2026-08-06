using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// A single mod dependency requirement declared by any content.
/// </summary>
public sealed class ModDependency
{
    /// <summary>
    /// The ID of the required mod.
    /// </summary>
    public string ModId { get; }

    /// <summary>
    /// The kind of dependency.
    /// </summary>
    // This may need a massive overhaul to handle the different kinds of dependencies, but for now, we will just use a simple enum.
    public ModDependencyKind Kind { get; }

    /// <summary>
    /// The minimum version of the mod, if any. For required, optional, recommends, and suggests, this will be the minimum version of the mod is compatible.
    /// For conflict, this will be the minimum version of the mod is incompatible.
    /// </summary>
    public ModVersion? MinVersion { get; }

    /// <summary>
    /// The maximum version of the mod, if any. For required, optional, recommends, and suggests, this will be the maximum version of the mod is compatible.
    /// For conflict, this will be the maximum version of the mod is incompatible.
    /// </summary>
    public ModVersion? MaxVersion { get; }

    /// <param name="kind">The kind of dependency.</param>
    /// <param name="minVersion">For required, optional, recommends, and suggests, this will be the minimum version of the mod is compatible.
    /// For conflict, this will be the minimum version of the mod is incompatible.</param>
    /// <param name="maxVersion">For required, optional, recommends, and suggests, this will be the maximum version of the mod is compatible.
    /// For conflict, this will be the maximum version of the mod is incompatible.</param>
    public ModDependency(string modId, ModDependencyKind kind, ModVersion? minVersion = null, ModVersion? maxVersion = null)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentException("Dependency mod id cannot be empty.", nameof(modId));
        } 

        ModId = modId;
        Kind = kind;
        MinVersion = minVersion;
        MaxVersion = maxVersion;
    }

    public override string ToString()
    {
        string versionRange = (MinVersion, MaxVersion) switch
        {
            (null, null) => "",
            (var min, null) => $" >= {min}",
            (null, var max) => $" <= {max}",
            (var min, var max) => $" >= {min} <= {max}"
        };
        return $"{Kind} dependency on mod '{ModId}'{versionRange}";
    }
}
