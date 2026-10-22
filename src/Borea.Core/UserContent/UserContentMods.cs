using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.State;

namespace Borea.Core.UserContent;

/// <summary>
/// The mods a vehicle or save needs enabled, for the single install and for the pins of a pack alike. The game treats a
/// mod that is not enabled as missing (ModLibrary.PrepareAll), so each one the instance holds disabled is enabled.
/// </summary>
public static class UserContentMods
{
    /// <summary>
    /// Every mod that a required dependency of an owner names, and the required dependencies of those, as the plan
    /// selects them or the instance holds them. For an any_of the alternative the plan resolved comes first, because the
    /// planner does not select an installed alternative.
    /// </summary>
    /// <param name="missing">Receives each required dependency of the first owner that no planned or installed mod satisfies.</param>
    public static IReadOnlyList<string> Needed(Instance instance, IReadOnlyList<ModVersionMetadata> owners, InstallPlan mods, List<ModDependency>? missing = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(mods);
        var needed = new List<string>();
        var pending = new Queue<ModVersionMetadata>(owners);
        var visited = new HashSet<string>(ModIds.Comparer);
        while (pending.TryDequeue(out var owner))
        {
            if (!visited.Add(owner.ModId))
                continue;

            for (var index = 0; index < owner.Dependencies.Count; index++)
            {
                var dependency = owner.Dependencies[index];
                if (dependency.Kind != ModDependencyKind.Required)
                    continue;

                var targets = dependency.IsAnyOf ? dependency.AnyOf.Select(value => value.ModId) : [dependency.ModId!];
                var present = targets.Where(id => Selected(mods, id) is not null || IsInstalled(instance, id)).ToList();
                if (present.Count == 0)
                {
                    if (owners.Count > 0 && ReferenceEquals(owner, owners[0]))
                        missing?.Add(dependency);
                    continue;
                }

                var resolved = mods.Choices.FirstOrDefault(choice => choice.Kind == PlanningChoiceKind.Alternative && choice.Dependency == dependency && ModIds.Equals(choice.OwnerModId, owner.ModId))?.Selected;
                var id = present.FirstOrDefault(value => needed.Contains(value, ModIds.Comparer))
                    ?? present.FirstOrDefault(value => ModIds.Equals(value, resolved))
                    ?? present.FirstOrDefault(value => Selected(mods, value) is not null)
                    ?? present[0];
                if (needed.Contains(id, ModIds.Comparer))
                    continue;

                needed.Add(id);
                if ((Selected(mods, id) ?? instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, id))?.Metadata) is { } next)
                    pending.Enqueue(next);
            }
        }

        return needed;
    }

    /// <summary>The mods of <paramref name="needed"/> that the instance holds and the game does not load now.</summary>
    public static IReadOnlyList<string> Enables(Instance instance, IEnumerable<string> needed, IReadOnlyList<ModManifestEntry> entries) =>
        needed
            .Where(id => IsInstalled(instance, id))
            .Where(id => !entries.Any(entry => entry.Enabled && ModIds.Equals(entry.ModId, id)))
            .ToList();

    /// <summary>A new entry goes in enabled, and an entry that exists is enabled, because <see cref="IModStateRepository.AddEntryAsync"/> keeps a disabled one.</summary>
    public static async Task EnableAsync(IModStateRepository modState, Guid instanceId, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modState);
        var entries = await modState.GetEntriesAsync(instanceId, cancellationToken).ConfigureAwait(false);
        foreach (var id in ids)
        {
            if (!entries.Any(entry => ModIds.Equals(entry.ModId, id)))
                await modState.AddEntryAsync(instanceId, id, enabled: true, cancellationToken).ConfigureAwait(false);
            else if (!entries.Any(entry => entry.Enabled && ModIds.Equals(entry.ModId, id)))
                await modState.SetActiveAsync(instanceId, id, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ModVersionMetadata? Selected(InstallPlan mods, string id) =>
        mods.Selections.FirstOrDefault(selection => ModIds.Equals(selection.Release.ModId, id))?.Release;

    private static bool IsInstalled(Instance instance, string id) =>
        instance.Mods.Any(mod => ModIds.Equals(mod.ModId, id)) || instance.ForeignMods.Any(mod => ModIds.Equals(mod.ModId, id));
}
