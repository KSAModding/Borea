using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.UserContent;

/// <param name="Dependency">The required entry of the item's release that the change breaks.</param>
public sealed record BrokenContent(InstalledContentRecord Record, ModDependency Dependency);

/// <summary>
/// Finds the recorded vehicles and saves that a mod change breaks (RFC 0085). The game refuses a vehicle
/// whose parts are missing and leaves it out of a save, so a removed, disabled or updated mod that a
/// required entry names breaks the item. An entry breaks only when it is met before the change and not
/// after it, so an item that is broken already gives no second warning.
/// </summary>
public static class RecordedContentNeeds
{
    /// <summary>The mods of an instance, by id. A foreign mod has a null version, because Borea cannot read it.</summary>
    public static IReadOnlyDictionary<string, ModVersion?> InstanceView(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var view = new Dictionary<string, ModVersion?>(ModIds.Comparer);
        foreach (var foreign in instance.ForeignMods)
            view[foreign.ModId] = null;
        foreach (var mod in instance.Mods)
            view[mod.ModId] = mod.Version;
        return view;
    }

    public static IReadOnlyDictionary<string, ModVersion?> Without(IReadOnlyDictionary<string, ModVersion?> view, string modId)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Where(mod => !ModIds.Equals(mod.Key, modId)).ToDictionary(mod => mod.Key, mod => mod.Value, ModIds.Comparer);
    }

    /// <summary>The view after the installs and updates of a plan.</summary>
    public static IReadOnlyDictionary<string, ModVersion?> With(IReadOnlyDictionary<string, ModVersion?> view, IEnumerable<PlannedInstall> operations)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(operations);

        var after = new Dictionary<string, ModVersion?>(view, ModIds.Comparer);
        foreach (var operation in operations)
            after[operation.Release.ModId] = operation.Release.Version;
        return after;
    }

    /// <summary>The required entries of the records that <paramref name="before"/> meets and <paramref name="after"/> does not, in record order.</summary>
    public static IReadOnlyList<BrokenContent> Broken(
        IEnumerable<InstalledContentRecord> records,
        IReadOnlyDictionary<string, ModVersion?> before,
        IReadOnlyDictionary<string, ModVersion?> after)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return records
            .SelectMany(record => record.Metadata.Dependencies
                .Where(dependency => dependency.Kind == ModDependencyKind.Required && IsMet(dependency, before) && !IsMet(dependency, after))
                .Select(dependency => new BrokenContent(record, dependency)))
            .ToList();
    }

    /// <summary>The records that the game no longer loads in full when <paramref name="modId"/> is removed or disabled, each one once.</summary>
    public static IReadOnlyList<InstalledContentRecord> Needing(IEnumerable<InstalledContentRecord> records, Instance instance, string modId)
    {
        var view = InstanceView(instance);
        return Broken(records, view, Without(view, modId)).Select(broken => broken.Record).Distinct().ToList();
    }

    // The same rule as the planner: a version inside the bounds, or a foreign mod when the entry has no bounds.
    private static bool IsMet(ModDependency dependency, IReadOnlyDictionary<string, ModVersion?> view) =>
        dependency.IsAnyOf
            ? dependency.AnyOf.Any(alternative => IsMet(alternative.ModId, alternative.MinVersion, alternative.MaxVersion, view))
            : IsMet(dependency.ModId, dependency.MinVersion, dependency.MaxVersion, view);

    private static bool IsMet(string modId, ModVersion? min, ModVersion? max, IReadOnlyDictionary<string, ModVersion?> view)
    {
        if (!view.TryGetValue(modId, out var version))
            return false;

        if (version is not { } known)
            return min is null && max is null;

        return (min is not { } low || known.CompareTo(low) >= 0) && (max is not { } high || known.CompareTo(high) <= 0);
    }
}
