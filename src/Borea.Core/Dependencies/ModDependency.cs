using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// A single dependency requirement declared by a mod, e.g. "requires CoolLib >=1.2.0".
/// A <see cref="ModMetadata"/> holds a list of these for everything it depends on.
/// </summary>
public sealed class ModDependency
{
    /// <summary>The <see cref="ModMetadata.ModId"/> of the required mod.</summary>
    public string ModId { get; }

    /// <summary>The version constraint the installed dependency must satisfy.</summary>
    public VersionRange RequiredVersion { get; }

    /// <summary>
    /// If true, this dependency is a "soft" requirement — the depending mod integrates
    /// with it when present but will still load without it. Resolution should not
    /// auto-install or block on optional dependencies the way it does for required ones.
    /// </summary>
    public bool IsOptional { get; }

    public ModDependency(string modId, VersionRange requiredVersion, bool isOptional = false)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentException("Dependency mod id cannot be empty.", nameof(modId));
        }

        ModId = modId;
        RequiredVersion = requiredVersion ?? throw new ArgumentNullException(nameof(requiredVersion));
        IsOptional = isOptional;
    }

    public override string ToString() => IsOptional ? $"{ModId} {RequiredVersion} (optional)" : $"{ModId} {RequiredVersion}";
}
