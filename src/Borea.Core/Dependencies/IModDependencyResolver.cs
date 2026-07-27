using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// Computes dependency relationships between mods within a specific instance.
/// </summary>
public interface IModDependencyResolver
{
    /// <summary>
    /// Determines which of a candidate mod's required dependencies are not
    /// currently satisfied within the given instance — either missing
    /// entirely, or present at a version that doesn't satisfy the declared
    /// VersionRange. Dependencies where IsOptional is true are excluded even
    /// when unsatisfied, since they don't block installation.
    /// </summary>
    IReadOnlyList<ModDependency> GetUnsatisfiedDependencies(Instance instance, ModMetadata candidate);

    /// <summary>
    /// Checks whether the given installed mod can safely be uninstalled from
    /// the instance, by scanning the instance's other installed mods for any
    /// whose declared dependencies this mod currently satisfies.
    /// </summary>
    /// <param name="isActive">
    /// Whether the mod is currently active per the instance's StarMap
    /// manifest.
    /// </param>
    UninstallCheck CheckUninstall(Instance instance, string modId, ModVersion version, bool isActive);
}