using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Dependencies;

/// <summary>
/// Computes dependency relationships between mods within a specific instance,
/// following the dependency kinds of RFC 0031.
/// </summary>
public sealed class ModDependencyResolver
{
    /// <summary>
    /// Weighs every entry that bears on installing a release into an instance.
    /// the ones the release declares, in document order.
    /// </summary>
    public IReadOnlyList<DependencyEvaluation> Evaluate(Instance instance, ModVersionMetadata candidate)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));

        var evaluations = new List<DependencyEvaluation>();

        foreach (var dependency in candidate.Dependencies)
            evaluations.Add(EvaluateDeclared(instance, dependency));

        return evaluations;
    }

    public IReadOnlyList<ModDependency> GetUnsatisfiedDependencies(Instance instance, ModVersionMetadata candidate)
        => Evaluate(instance, candidate)
            .Where(e => e.Outcome == DependencyOutcome.Install)
            .Select(e => e.Dependency)
            .ToList();

    /// <summary>
    /// Checks if a mod can be uninstalled from an instance, returning the list of dependent mods that would be affected.
    /// </summary>
    public UninstallCheck CheckUninstall(Instance instance, string modId, ModVersion version, bool isActive)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        var dependents = instance.Mods
            .Where(m => !ModIds.Equals(m.ModId, modId))
            .Where(m => m.Metadata.Dependencies.Any(d => WouldBreakOnRemoval(instance, d, modId, version)))
            .Select(m => m.ModId)
            .ToList();

        return new UninstallCheck(instance.InstanceId, modId, version, dependents, isActive);
    }

    private static DependencyEvaluation EvaluateDeclared(Instance instance, ModDependency dependency)
    {
        if (dependency.Kind == ModDependencyKind.Unknown)
            return new DependencyEvaluation(dependency, DependencyOutcome.Unknown);

        if (dependency.IsAnyOf)
        {
            foreach (var alternative in dependency.AnyOf)
            {
                var alternativeMatch = FindInstalled(instance, alternative.ModId);
                if (alternativeMatch is not null && alternative.BoundsContain(alternativeMatch.Version))
                    return new DependencyEvaluation(dependency, DependencyOutcome.Satisfied, installedModId: alternativeMatch.ModId);
            }

            return new DependencyEvaluation(dependency, MissingOutcome(dependency.Kind));
        }

        var match = FindInstalled(instance, dependency.ModId);

        if (match is null || !dependency.BoundsContain(match.Version))
        {
            return dependency.Kind == ModDependencyKind.Conflict
                ? new DependencyEvaluation(dependency, DependencyOutcome.Satisfied)
                : new DependencyEvaluation(dependency, MissingOutcome(dependency.Kind));
        }

        return dependency.Kind == ModDependencyKind.Conflict
            ? new DependencyEvaluation(dependency, DependencyOutcome.Conflict, installedModId: match.ModId)
            : new DependencyEvaluation(dependency, DependencyOutcome.Satisfied, installedModId: match.ModId);
    }

    private static DependencyOutcome MissingOutcome(ModDependencyKind kind) => kind switch
    {
        ModDependencyKind.Required => DependencyOutcome.Install,
        ModDependencyKind.Recommends => DependencyOutcome.SelectByDefault,
        ModDependencyKind.Optional or ModDependencyKind.Suggests => DependencyOutcome.Offer,
        _ => DependencyOutcome.Unknown,
    };

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
