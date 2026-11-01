using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// Explains a plan that stopped on mods that do not work together. The row or the plan modal keeps the text of the planner,
/// so this modal only adds who declares the conflict and the way out.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConflictOpen))]
    private ConflictDialog? _conflictDialog;

    public bool IsConflictOpen => ConflictDialog is not null;

    /// <summary>Opens the modal when the plan names a conflict entry. Any other reason of a stopped plan stays only as the text of the planner.</summary>
    /// <param name="again">Plans the same change again after the removal. Without it the modal offers only the page of the other mod.</param>
    private async Task ShowConflictsAsync(InstallPlan plan, Func<Task>? again)
    {
        var conflicts = plan.Conflicts.Concat(plan.UnresolvedChoices).Where(NamesAnotherMod).ToList();
        if (conflicts.Count == 0)
            return;

        var needs = await InstanceNeedsOfAsync(plan.InstanceId);
        var rows = new List<ConflictRow>();
        foreach (var conflict in conflicts.DistinctBy(ModPair))
            rows.Add(NewConflictRow(plan, conflict.ModId, conflict.Dependency!, needs, again));

        ConflictDialog = new ConflictDialog(this, rows);
    }

    // the planner also reports a conflict entry of an installed mod under the mod it names, and there the entry names the mod itself, so only the other report says who declares it
    private static bool NamesAnotherMod(PlanningMessage message)
        => message.Dependency is { Kind: ModDependencyKind.Conflict, ModId: { } named } && !ModIds.Equals(message.ModId, named);

    private static (string First, string Second) ModPair(PlanningMessage conflict)
    {
        var (declarer, named) = (conflict.ModId.ToUpperInvariant(), conflict.Dependency!.ModId!.ToUpperInvariant());
        return string.CompareOrdinal(declarer, named) < 0 ? (declarer, named) : (named, declarer);
    }

    /// <summary>What the removal of a mod would break. A plan for an instance that does not exist yet has none, and so no mod to remove.</summary>
    private async Task<InstanceNeeds?> InstanceNeedsOfAsync(Guid instanceId)
    {
        if (_services is not { } services)
            return null;

        try
        {
            return await services.Instances.GetByIdAsync(instanceId) is { } instance
                ? new InstanceNeeds(instance, await InstalledContentOfAsync(instanceId), await InactiveModIdsAsync(instanceId))
                : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private ConflictRow NewConflictRow(InstallPlan plan, string declarer, ModDependency entry, InstanceNeeds? needs, Func<Task>? again)
    {
        var named = entry.ModId!;
        var otherId = IsKept(plan, needs, named) || !IsKept(plan, needs, declarer) ? named : declarer;
        var name = ContentName(otherId);
        var isKept = IsKept(plan, needs, otherId);
        var reason = isKept ? WhyItStays(plan, needs!, otherId, entry) : null;
        var canRemove = isKept && reason is null && again is not null;
        var listing = canRemove ? null : _listings.FirstOrDefault(item => ModIds.Equals(item.ModId, otherId));
        return new ConflictRow(
            Localization.FormatConflictDeclared(ContentName(declarer), ContentName(named)),
            reason,
            canRemove ? Localization.FormatConflictRemove(name) : null,
            listing is null ? null : Localization.FormatConflictOpenPage(name),
            () => RemoveConflictingModAsync(plan.InstanceId, otherId, again!),
            () => OpenConflictingModAsync(listing!));
    }

    /// <summary>Whether the instance holds the mod and the plan does not change it, which makes it the mod that stands in the way of the change.</summary>
    private static bool IsKept(InstallPlan plan, InstanceNeeds? needs, string modId)
        => needs is { Instance: var instance }
            && (instance.Mods.Any(mod => ModIds.Equals(mod.ModId, modId)) || instance.ForeignMods.Any(mod => ModIds.Equals(mod.ModId, modId)))
            && !plan.Operations.Any(operation => ModIds.Equals(operation.Release.ModId, modId));

    /// <summary>
    /// Why the modal does not offer to remove a mod the instance holds. A vehicle or save counts here, although it does not block a removal on
    /// the instance page, because a player who only wanted to change another mod did not ask to lose it. A mod of the pack counts too, because the next
    /// update of the pack brings it back.
    /// </summary>
    private string? WhyItStays(InstallPlan plan, InstanceNeeds needs, string modId, ModDependency entry)
    {
        var name = ContentName(modId);
        if (needs.Instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId)) is not { } mod)
            return entry.MinVersion is null && entry.MaxVersion is null ? Localization.FormatContentRemoveNotOwned(name) : Localization.FormatConflictVersionUnknown(name);

        if (mod.Ownership != ModInstallOwnership.Borea)
            return Localization.FormatContentRemoveNotOwned(name);

        if (mod.Reason == InstallReason.ModPack)
            return Localization.FormatConflictPackMember(name);

        var check = new ModDependencyResolver().CheckUninstall(needs.Instance, mod.ModId, mod.Version, isActive: false, needs.Content, needs.Inactive);
        var planned = plan.Selections
            .Where(selection => !ModIds.Equals(selection.Release.ModId, modId)
                && selection.Release.Dependencies.Any(dependency => dependency.Kind == ModDependencyKind.Required && ModIds.Equals(dependency.ModId, modId)))
            .Select(selection => selection.Release.ModId);
        var needing = check.DependentModIds.Concat(planned).Select(ContentName)
            .Concat(check.DependentContent.Select(record => record.FolderName))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return needing.Count == 0 ? null : Localization.FormatContentRemoveRequired(name, string.Join(", ", needing));
    }

    /// <summary>A removal that fails or does not run says so in its own toast, so the plan does not run again.</summary>
    private async Task RemoveConflictingModAsync(Guid instanceId, string modId, Func<Task> again)
    {
        ConflictDialog = null;
        if (await RemoveContentAsync(instanceId, modId))
            await again();
    }

    /// <summary>A plan modal below would cover the page, and its plan cannot run anyway.</summary>
    private Task OpenConflictingModAsync(DiscoverItem listing)
    {
        ConflictDialog = null;
        if (ModListImport is { } review)
            CancelModList(review);

        return OpenContentAsync(listing);
    }

    private sealed record InstanceNeeds(Instance Instance, IReadOnlyList<InstalledContentRecord> Content, IReadOnlyList<string> Inactive);
}
