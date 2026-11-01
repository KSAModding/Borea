using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.App.ViewModels;

/// <summary>
/// The steps of RFC 0035: what a listing asks the player to do by hand before playing
/// and when the content goes. They are the author's prose, so the pages show them as
/// plain text and never run, parse or link anything in them.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The mods whose install steps a finished install left on the row of a mod, by the id of that mod.</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _installStepsNotices = new(ModIds.Comparer);

    /// <summary>The instance that got a fresh vehicle or save, by the id of its listing, until the player dismisses the steps.</summary>
    private readonly Dictionary<string, Guid> _installedWhereNotices = new(ModIds.Comparer);

    /// <summary>The mods whose install steps a finished pack install left on the row of a pack, by the id of that pack.</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _packInstallStepsNotices = new(ModIds.Comparer);

    /// <summary>A numbered list under its heading, or null when the author gave no steps or an empty list.</summary>
    internal static StepList? Steps(string heading, IReadOnlyList<string>? steps)
        => steps is { Count: > 0 }
            ? new StepList(heading, steps.Select((text, index) => new NumberedStep($"{index + 1}.", text)).ToList())
            : null;

    /// <summary>The install steps of every listing the plan installs, one list for each listing that has steps.</summary>
    internal IReadOnlyList<StepList> InstallStepsOf(InstallPlan? plan)
        => InstallStepsOf(plan?.Operations.Select(operation => operation.Release.ModId) ?? []);

    private IReadOnlyList<StepList> InstallStepsOf(IEnumerable<string> modIds)
        => modIds
            .Select(modId => Steps(Localization.FormatInstallStepsFor(ContentName(modId)), ListingOf(modId)?.Install?.Steps))
            .OfType<StepList>()
            .ToList();

    private ModMetadata? ListingOf(string modId)
        => _listings.FirstOrDefault(item => ModIds.Equals(item.ModId, modId))?.Listing;

    /// <summary>The install steps that a finished install left on the row of <paramref name="modId"/>, and where a new vehicle or save is in the game.</summary>
    internal IReadOnlyList<StepList> InstallStepsNotice(string modId)
    {
        var steps = _installStepsNotices.TryGetValue(modId, out var installed) ? InstallStepsOf(installed) : [];
        return InstalledWhereSteps(modId) is { } where ? [.. steps, where] : steps;
    }

    /// <summary>
    /// The instance and the record of a fresh vehicle or save. The record gives the current folder, and when the
    /// player removes the item or its instance there is no record, so the steps stop naming something that is gone.
    /// </summary>
    private (InstanceItem Instance, InstalledContentRecord Record)? InstalledWhere(string modId)
    {
        if (!_installedWhereNotices.TryGetValue(modId, out var instanceId)
            || Instances.FirstOrDefault(item => item.InstanceId == instanceId) is not { } instance)
        {
            return null;
        }

        return instance.Content.FirstOrDefault(record => ModIds.Equals(record.ListingId, modId)) is { } found ? (instance, found) : null;
    }

    /// <summary>The menu names of the game stay English in every language, because the game shows them so.</summary>
    private StepList? InstalledWhereSteps(string modId)
    {
        if (InstalledWhere(modId) is not { Instance: var instance, Record: var record })
            return null;

        var name = record.FolderName;
        string[] steps = record.Metadata.Type == ContentType.Save
            ? [Localization.FormatInstalledStart(instance.Name), Localization.InstalledSaveMenu, Localization.FormatInstalledSavePick(name)]
            : [Localization.FormatInstalledStart(instance.Name), Localization.InstalledVehicleMenu, Localization.FormatInstalledVehiclePick(name)];
        return Steps(Localization.FormatInstalledWhere(name), steps);
    }

    /// <summary>The text of the Play button of the steps, or null when the row shows no such steps.</summary>
    internal string? InstalledPlayText(string modId)
        => InstalledWhere(modId) is { Instance: var instance } ? Localization.FormatInstalledPlay(instance.Name) : null;

    internal bool CanPlayInstalled(string modId)
        => !IsLaunching && _services is { } services && InstalledWhere(modId) is { Instance: var instance } && !services.Launcher.IsRunning(instance.InstanceId);

    internal Task PlayInstalledAsync(string modId)
        => InstalledWhere(modId) is { Instance: var instance } ? LaunchAsync(instance.InstanceId) : Task.CompletedTask;

    private void LeaveInstalledWhereNotice(string modId, Guid instanceId)
    {
        _installedWhereNotices[modId] = instanceId;
        RefreshInstallStepsNotice(modId);
    }

    /// <summary>The steps follow the records and the instances, and Play follows the launches.</summary>
    private void RefreshInstalledWhereNotices()
    {
        foreach (var modId in _installedWhereNotices.Keys)
            RefreshInstallStepsNotice(modId);
    }

    partial void OnIsLaunchingChanged(bool value) => RefreshInstalledWhereNotices();

    /// <summary>
    /// Keeps the install steps of a finished plan on the Discover row and the page of the
    /// mod the player added, until the player dismisses them.
    /// </summary>
    private void LeaveInstallStepsNotice(IInstallRow row, InstallPlan plan)
    {
        var modId = row switch
        {
            DiscoverItem item => item.ModId,
            VersionItem version => version.ModId,
            PackMemberItem member => member.ModId,
            _ => null,
        };
        var withSteps = plan.Operations
            .Select(operation => operation.Release.ModId)
            .Where(id => ListingOf(id)?.Install?.Steps is { Count: > 0 })
            .ToList();
        if (modId is null || withSteps.Count == 0)
            return;

        _installStepsNotices[modId] = withSteps;
        RefreshInstallStepsNotice(modId);
    }

    internal void DismissInstallStepsNotice(string modId)
    {
        var hadSteps = _installStepsNotices.Remove(modId);
        var hadWhere = _installedWhereNotices.Remove(modId);
        if (hadSteps || hadWhere)
            RefreshInstallStepsNotice(modId);
    }

    private void RefreshInstallStepsNotice(string modId)
        => _listings.FirstOrDefault(item => ModIds.Equals(item.ModId, modId))?.RefreshInstallStepsNotice();

    /// <summary>The install steps that a finished install of the pack left on its row.</summary>
    internal IReadOnlyList<StepList> PackInstallStepsNotice(string packId)
        => _packInstallStepsNotices.TryGetValue(packId, out var installed) ? InstallStepsOf(installed) : [];

    /// <summary>
    /// Keeps the install steps of the members that a pack install added or replaced on the row
    /// of the pack, in the order of its plan, until the player dismisses them. A stopped or an
    /// incomplete install counts too, because the members it added still ask for their steps.
    /// </summary>
    private void LeavePackInstallStepsNotice(PackItem pack, ModPackInstallResult result)
    {
        var added = result.Members
            .Where(member => member.Status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced)
            .Select(member => member.ModId)
            .ToHashSet(ModIds.Comparer);
        var planned = result.Plan?.Operations.Select(operation => operation.Release.ModId) ?? [];
        var withSteps = planned
            .Concat(added)
            .Where(added.Contains)
            .Distinct(ModIds.Comparer)
            .Where(id => ListingOf(id)?.Install?.Steps is { Count: > 0 })
            .ToList();
        if (withSteps.Count == 0)
            return;

        _packInstallStepsNotices[pack.PackId] = withSteps;
        RefreshPackInstallStepsNotice(pack.PackId);
    }

    internal void DismissPackInstallStepsNotice(string packId)
    {
        if (_packInstallStepsNotices.Remove(packId))
            RefreshPackInstallStepsNotice(packId);
    }

    private void RefreshPackInstallStepsNotice(string packId)
    {
        foreach (var pack in _packs.Append(SelectedPack).OfType<PackItem>().Where(item => ModIds.Equals(item.PackId, packId)).Distinct())
            pack.RefreshInstallStepsNotice();
    }
}

/// <summary>Steps of a listing under their heading, such as "Before you play".</summary>
public sealed record StepList(string Heading, IReadOnlyList<NumberedStep> Steps);

/// <summary>One step as the author wrote it, with its number.</summary>
public sealed record NumberedStep(string Number, string Text);
