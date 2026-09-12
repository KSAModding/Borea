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
    /// the ones the release declares, in document order, then the ones installed
    /// mods declare against it. group by <see cref="DependencyEvaluation.InstalledModId"/>.
    /// </summary>
    public IReadOnlyList<DependencyEvaluation> Evaluate(Instance instance, ModVersionMetadata candidate)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));

        var evaluations = new List<DependencyEvaluation>();

        foreach (var dependency in candidate.Dependencies)
            evaluations.Add(EvaluateDeclared(instance, dependency));

        evaluations.AddRange(DeclaredAgainst(instance, candidate));

        return evaluations;
    }

    public IReadOnlyList<ModDependency> GetUnsatisfiedDependencies(Instance instance, ModVersionMetadata candidate)
        => Evaluate(instance, candidate)
            .Where(e => e.Outcome == DependencyOutcome.Install)
            .Select(e => e.Dependency)
            .ToList();

    public IReadOnlyList<LocalDependencyEvaluation> EvaluateForeign(Instance instance, ForeignMod foreignMod)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(foreignMod);

        return foreignMod.Dependencies
            .Select(dependency =>
            {
                var installedModId = FindLocalDependency(instance, foreignMod, dependency.ModId);
                return installedModId is null
                    ? new LocalDependencyEvaluation(
                        dependency,
                        dependency.Optional ? DependencyOutcome.Offer : DependencyOutcome.Install)
                    : new LocalDependencyEvaluation(dependency, DependencyOutcome.Satisfied, installedModId);
            })
            .ToList();
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
            .Where(m => m.Metadata.Dependencies.Any(d => WouldBreakOnRemoval(instance, d, modId, version)))
            .Select(m => m.ModId)
            .ToList();

        dependents.AddRange(instance.ForeignMods
            .Where(mod => !ModIds.Equals(mod.ModId, modId))
            .Where(mod => mod.Dependencies.Any(dependency => !dependency.Optional && ModIds.Equals(dependency.ModId, modId)))
            .Select(mod => mod.ModId));

        return new UninstallCheck(instance.InstanceId, modId, version, dependents, isActive);
    }

    private static DependencyEvaluation EvaluateDeclared(Instance instance, ModDependency dependency)
    {
        if (dependency.Kind == ModDependencyKind.Unknown)
            return new DependencyEvaluation(dependency, DependencyOutcome.Unknown);

        if (dependency.IsAnyOf)
        {
            string? unknownAlternativeId = null;
            foreach (var alternative in dependency.AnyOf)
            {
                var alternativeMatch = FindInstalled(instance, alternative.ModId);
                if (alternativeMatch is not null && alternative.BoundsContain(alternativeMatch.Version))
                    return new DependencyEvaluation(dependency, DependencyOutcome.Satisfied, installedModId: alternativeMatch.ModId);

                if (FindForeign(instance, alternative.ModId) is { } foreign)
                {
                    if (alternative.MinVersion is null && alternative.MaxVersion is null)
                        return new DependencyEvaluation(dependency, DependencyOutcome.Satisfied, installedModId: foreign.ModId);

                    unknownAlternativeId = foreign.ModId;
                }
            }

            if (unknownAlternativeId is not null)
                return new DependencyEvaluation(dependency, DependencyOutcome.Unknown, installedModId: unknownAlternativeId);

            return new DependencyEvaluation(dependency, MissingOutcome(dependency.Kind));
        }

        var match = FindInstalled(instance, dependency.ModId);

        if (match is null && FindForeign(instance, dependency.ModId) is { } foreignMatch)
        {
            if (dependency.MinVersion is not null || dependency.MaxVersion is not null)
                return new DependencyEvaluation(dependency, DependencyOutcome.Unknown, installedModId: foreignMatch.ModId);

            return dependency.Kind == ModDependencyKind.Conflict
                ? new DependencyEvaluation(dependency, DependencyOutcome.Conflict, installedModId: foreignMatch.ModId)
                : new DependencyEvaluation(dependency, DependencyOutcome.Satisfied, installedModId: foreignMatch.ModId);
        }

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

    private static IEnumerable<DependencyEvaluation> DeclaredAgainst(Instance instance, ModVersionMetadata candidate)
    {
        foreach (var installed in instance.Mods)
        {
            // An update replaces the copy of the same id
            if (ModIds.Equals(installed.ModId, candidate.ModId))
                continue;

            foreach (var dependency in installed.Metadata.Dependencies)
            {
                // An any_of entry has no single id
                if (!ModIds.Equals(dependency.ModId, candidate.ModId))
                    continue;

                if (dependency.Kind == ModDependencyKind.Unknown)
                {
                    yield return new DependencyEvaluation(dependency, DependencyOutcome.Unknown, declaredBy: installed.ModId);
                    continue;
                }

                if (dependency.Kind != ModDependencyKind.Conflict || !dependency.BoundsContain(candidate.Version))
                    continue;

                yield return new DependencyEvaluation(
                    dependency,
                    DependencyOutcome.Conflict,
                    declaredBy: installed.ModId,
                    installedModId: installed.ModId);
            }
        }
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

            return !dependency.AnyOf.Any(a => !ModIds.Equals(a.ModId, removedId) && AlternativeIsSatisfied(instance, a));
        }

        return ModIds.Equals(dependency.ModId, removedId) && dependency.BoundsContain(removedVersion);
    }

    private static InstalledMod? FindInstalled(Instance instance, string modId)
        => instance.Mods.FirstOrDefault(m => ModIds.Equals(m.ModId, modId));

    private static ForeignMod? FindForeign(Instance instance, string modId)
        => instance.ForeignMods.FirstOrDefault(m => ModIds.Equals(m.ModId, modId));

    private static string? FindLocalDependency(Instance instance, ForeignMod dependent, string modId)
    {
        var installed = FindInstalled(instance, modId);
        if (installed is not null)
            return installed.ModId;

        return instance.ForeignMods
            .Where(mod => !ReferenceEquals(mod, dependent))
            .FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId))
            ?.ModId;
    }

    private static bool AlternativeIsSatisfied(Instance instance, ModDependencyAlternative alternative)
    {
        if (FindInstalled(instance, alternative.ModId) is { } installed)
            return alternative.BoundsContain(installed.Version);

        return alternative.MinVersion is null
            && alternative.MaxVersion is null
            && FindForeign(instance, alternative.ModId) is not null;
    }
}
