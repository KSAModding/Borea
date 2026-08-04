using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// Computes dependency relationships between mods within a specific instance.
/// </summary>
public sealed class ModDependencyResolver
{
    /// <summary>
    /// Gets the list of unsatisfied dependencies for a given mod within an instance.
    /// Optional dependencies are ignored, as their absence does not block installation.
    /// </summary>
    public IReadOnlyList<ModDependency> GetUnsatisfiedDependencies(Instance instance, ModMetadata candidate)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));

        var unsatisfied = new List<ModDependency>();

        foreach (var dependency in candidate.Dependencies)
        {
            if (dependency.IsOptional)
                continue;

            var installed = instance.Mods.FirstOrDefault(m => string.Equals(m.ModId, dependency.ModId, StringComparison.Ordinal));

            if (installed is null || !dependency.RequiredVersion.Satisfies(installed.Version))
                unsatisfied.Add(dependency);
        }

        return unsatisfied;
    }

    /// <summary>
    /// Checks if a mod can be uninstalled from an instance, returning the list of dependent mods that would be affected.
    /// Optional dependencies are ignored, as their removal does not block uninstallation.
    /// </summary>
    public UninstallCheck CheckUninstall(Instance instance, string modId, ModVersion version, bool isActive)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        var dependents = instance.Mods
            .Where(m => !string.Equals(m.ModId, modId, StringComparison.Ordinal))
            .Where(m => m.Metadata.Dependencies.Any(d =>
                !d.IsOptional && // A mod only optionally depending on this one isn't blocked by its removal.
                string.Equals(d.ModId, modId, StringComparison.Ordinal) &&
                d.RequiredVersion.Satisfies(version)))
            .Select(m => m.ModId)
            .ToList();

        return new UninstallCheck(instance.InstanceId, modId, version, dependents, isActive);
    }
}
