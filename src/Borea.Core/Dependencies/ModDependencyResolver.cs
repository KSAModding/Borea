using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// Computes dependency relationships between mods within a specific instance.
/// Evaluates required entries; the other kinds are carried but not acted on.
/// </summary>
public sealed class ModDependencyResolver
{
    /// <summary>
    /// Gets the list of unsatisfied required dependencies for a release within an instance.
    /// </summary>
    public IReadOnlyList<ModDependency> GetUnsatisfiedDependencies(Instance instance, ModVersionMetadata candidate)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));

        var unsatisfied = new List<ModDependency>();

        foreach (var dependency in candidate.Dependencies)
        {
            if (dependency.Kind != ModDependencyKind.Required)
                continue;

            if (!IsSatisfied(instance, dependency))
                unsatisfied.Add(dependency);
        }

        return unsatisfied;
    }

    /// <summary>
    /// Checks if a mod can be uninstalled from an instance, returning the list of dependent mods that would be affected.
    /// </summary>
    public UninstallCheck CheckUninstall(Instance instance, string modId, ModVersion version, bool isActive)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        var dependents = instance.Mods
            .Where(m => !ModIds.Equals(m.ModId, modId))
            .Where(m => m.Dependencies.Any(d => WouldBreakOnRemoval(instance, d, modId, version)))
            .Select(m => m.ModId)
            .ToList();

        return new UninstallCheck(instance.InstanceId, modId, version, dependents, isActive);
    }

    private static bool IsSatisfied(Instance instance, ModDependency dependency)
    {
        if (dependency.IsAnyOf)
            return dependency.AnyOf.Any(a => FindInstalled(instance, a.ModId) is { } m && a.BoundsContain(m.Version));

        return FindInstalled(instance, dependency.ModId) is { } installed && dependency.BoundsContain(installed.Version);
    }

    private static bool WouldBreakOnRemoval(Instance instance, ModDependency dependency, string removedId, ModVersion removedVersion)
    {
        if (dependency.Kind != ModDependencyKind.Required)
            return false;

        if (dependency.IsAnyOf)
        {
            // Removal only breaks an any_of entry when the removed mod was the
            // last installed alternative that satisfied it.
            bool removedSatisfies = dependency.AnyOf.Any(a => ModIds.Equals(a.ModId, removedId) && a.BoundsContain(removedVersion));
            if (!removedSatisfies)
                return false;

            return !dependency.AnyOf.Any(a => !ModIds.Equals(a.ModId, removedId) && FindInstalled(instance, a.ModId) is { } m && a.BoundsContain(m.Version));
        }

        return ModIds.Equals(dependency.ModId, removedId) && dependency.BoundsContain(removedVersion);
    }

    private static InstalledMod? FindInstalled(Instance instance, string modId)
        => instance.Mods.FirstOrDefault(m => ModIds.Equals(m.ModId, modId));
}
