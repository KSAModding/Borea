using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The notice on the instance page when the index has a newer version of the instance's pack.</summary>
public partial class MainViewModel
{
    [ObservableProperty]
    private PackUpdateItem? _packUpdate;

    private async Task<PackUpdateItem?> FindPackUpdateAsync(Instance? instance)
    {
        if (_services is null || instance is null)
            return null;

        try
        {
            var newer = await ModPackUpdates.FindNewerAsync(_services.ModPacks, instance.Source);
            return newer?.Metadata is null ? null : new PackUpdateItem(this, instance.InstanceId, newer);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Shows the notice on its instance's page, unless a pack update runs there or <paramref name="keepSameVersion"/> keeps the shown notice of the same version.</summary>
    private void ShowPackUpdate(Guid instanceId, PackUpdateItem? found, bool keepSameVersion = false)
    {
        if (SelectedInstance?.InstanceId != instanceId || _runningUpdates.GetValueOrDefault(instanceId) is PackUpdateItem)
            return;

        if (keepSameVersion && PackUpdate is { } shown && found is not null && shown.InstanceId == instanceId
            && shown.Version == found.Version && shown.Pack.VersionStatus?.State == found.Pack.VersionStatus?.State)
            return;

        PackUpdate = found;
    }

    internal Task UpdatePackAsync(PackUpdateItem item)
        => RunUpdateAsync(item, item.InstanceId, () => PlanPackUpdateAsync(item));

    internal Task ConfirmPackUpdateAsync(PackUpdateItem item)
        => RunUpdateAsync(item, item.InstanceId, () => ExecutePackUpdateAsync(item));

    /// <summary>Returns false, because a planned update always waits for its confirmation.</summary>
    private async Task<bool> PlanPackUpdateAsync(PackUpdateItem item)
    {
        if (_services is not { } services || item.IsInstalling)
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            item.InstallError = Localization.LibraryFolderBusy;
            return false;
        }

        item.ClearOutcome();
        item.IsInstalling = true;
        var run = item.Run = StartInstallRun(StartTask(TaskKind.PackUpdate, item.PackName, item.InstanceId, item.PackId, state: TaskState.Waiting));
        string? stopped = null;
        try
        {
            var metadata = item.Pack.Metadata!;
            var installed = services.InstalledVersion.GetInstalledVersion()?.Version;
            var compatibility = Borea.Core.Game.Compatibility.Evaluate(metadata, installed, _gameReleases);
            if (compatibility == GameCompatibility.Incompatible)
                throw new InvalidOperationException(Localization.FormatPackIncompatible(metadata.GameMin));

            var yanked = new Dictionary<string, ModVersionMetadata>(ModIds.Comparer);
            foreach (var pin in metadata.Mods)
            {
                if (await services.Mods.GetReleaseAsync(pin.ContentId, pin.Version) is { Yanked: true } release)
                    yanked[pin.ContentId] = release;
            }

            var request = new ModPackUpdateRequest(item.InstanceId, item.Pack, services.Mods, installed, CurrentPlatform(), ProceedWithYankedMembers: yanked.Count == 0 ? null : yanked.Keys.ToHashSet(ModIds.Comparer));
            var (pending, result) = await PlanPackUpdateWithChoicesAsync(services, request, null);
            var reasons = PackWarnings(item.Pack, metadata, compatibility);
            foreach (var change in result.Changes.Where(change => change.Kind is ModPackChangeKind.Add or ModPackChangeKind.Change))
            {
                if (yanked.TryGetValue(change.ModId, out var release))
                    reasons.Add(Localization.FormatPackMemberYanked(release.ModId, release.Version.ToString(), release.YankedReason));
            }

            var choices = result.Plan is { } plan && InstallChoices.AreNeeded(plan) ? new InstallChoices(item.InstanceId, [], ContentName) : null;
            choices?.Apply(result.Plan!);
            if (run.InstallStop.IsRequested)
                stopped = StoppedText(item);
            else if (!result.CanRun && choices is null)
                item.InstallError = BlockedText(result);
            else
            {
                item.PendingReasons = reasons;
                item.Choices = choices;
                HoldPackUpdate(item, pending, result);
            }
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            item.InstallError = exception.Message;
        }
        finally
        {
            EndInstallRun(run, completed: false, stopped is not null, item.InstallError);
            item.EndInstall(stopped);
        }

        return false;
    }

    /// <summary>Plans with the user's choices and selects every recommendation the user has not deselected.</summary>
    private static async Task<(ModPackUpdateRequest Request, ModPackUpdateResult Result)> PlanPackUpdateWithChoicesAsync(BoreaServices services, ModPackUpdateRequest request, InstallChoices? choices)
    {
        var recommended = new HashSet<string>(choices?.SelectedRecommendations ?? new HashSet<string>(), StringComparer.Ordinal);
        var deselected = choices?.DeselectedRecommendations ?? new HashSet<string>();
        request = request with { Alternatives = choices?.SelectedAlternatives };
        while (true)
        {
            var current = request with { Recommended = new HashSet<string>(recommended, StringComparer.Ordinal) };
            var result = await services.ModPackUpdater.PlanAsync(current);
            var added = false;
            foreach (var choice in result.Plan?.Choices.Where(choice => choice.Kind == PlanningChoiceKind.Recommendation && !deselected.Contains(choice.Key)) ?? [])
                added |= recommended.Add(choice.Key);

            if (!added)
                return (current, result);
        }
    }

    private void HoldPackUpdate(PackUpdateItem item, ModPackUpdateRequest request, ModPackUpdateResult result)
    {
        var reasons = item.PendingReasons.ToList();
        if (result.Plan is { } plan && PackPlanWarnings(plan) is { Count: > 0 } warnings)
            reasons.Add(Describe(warnings));

        item.PendingRequest = request;
        item.PendingResult = result;
        item.PendingPlan = result.Plan;
        item.InstallWarning = reasons.Count > 0 ? string.Join(" ", reasons.Distinct()) : null;
        if (item.Choices is { } choices)
            choices.BlockedText = result.CanRun ? null : BlockedText(result);
    }

    private static string BlockedText(ModPackUpdateResult result)
        => result.Plan is { } plan ? Describe(plan.Conflicts.Concat(plan.UnresolvedChoices)) : Describe(result.Warnings);

    /// <summary>Runs the update the notice holds, unless the plan with its choices asks something new, cannot run, or has a new warning.</summary>
    private async Task<bool> ExecutePackUpdateAsync(PackUpdateItem item)
    {
        if (_services is not { } services || item.PendingRequest is not { } request || item.IsInstalling)
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            if (item.Choices is { } shown)
                shown.BlockedText = Localization.LibraryFolderBusy;
            else
                item.InstallError = Localization.LibraryFolderBusy;
            return false;
        }

        item.IsInstalling = true;
        var run = item.Run = StartInstallRun(StartTask(TaskKind.PackUpdate, item.PackName, item.InstanceId, item.PackId, state: TaskState.Waiting));
        var executed = false;
        var completed = false;
        string? stopped = null;
        string? error = null;
        try
        {
            var planned = item.PendingResult;
            if (item.Choices is { } choices)
            {
                var shown = planned?.Warnings ?? [];
                (request, planned) = await PlanPackUpdateWithChoicesAsync(services, request, choices);
                if ((planned.Plan is { } plan && choices.Apply(plan)) || !planned.CanRun || !planned.Warnings.All(shown.Contains))
                {
                    HoldPackUpdate(item, request, planned);
                    return false;
                }
            }

            item.CancelUpdate();
            executed = true;
            run.TaskItem.NewVersion = item.Version;
            if (planned?.Plan is { } running)
                run.TaskItem.MarkRunning(running);
            var result = await services.ModPackUpdater.UpdateAsync(request, ProgressOf(item), run.InstallStop);
            var steps = result.Members.Where(member => member.Status != ModPackMemberStatus.AlreadyInstalled).ToList();
            var done = steps.Count(member => member.Status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced or ModPackMemberStatus.Removed);
            if (result.IsStopped)
            {
                run.TaskItem.StoppedAfter = (done, steps.Count);
                stopped = StoppedText(item, done, steps.Count);
            }
            else if (!result.IsComplete)
            {
                var summary = Localization.FormatPackIncomplete(steps.Count - done, steps.Count);
                var details = result.Plan is null ? string.Empty : Describe(result.Plan.Conflicts.Concat(result.Plan.UnresolvedChoices));
                error = item.InstallError = details.Length == 0 ? summary : $"{summary} {details}";
            }

            completed = true;
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = exception.Message;
            if (item.Choices is { } choices)
                choices.BlockedText = error;
            else
                item.InstallError = error;
        }
        finally
        {
            EndInstallRun(run, completed, stopped is not null, error);
            item.EndInstall(stopped);
        }

        return executed;
    }
}

/// <summary>The newer version of the instance's pack, with its plan and outcome the way an update row holds them.</summary>
public sealed partial class PackUpdateItem : ObservableObject, IInstallRow
{
    private readonly MainViewModel _owner;

    internal Guid InstanceId { get; }

    internal ModPackResult Pack { get; }

    internal string PackId => Pack.Id;

    public string PackName => Pack.Metadata!.Name;

    public string Version => Pack.Metadata!.Version.ToString();

    public string NoticeText => _owner.Localization.FormatPackUpdateAvailable(PackName, Version);

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressStatus;

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private InstallRun? _run;

    [ObservableProperty]
    private string? _installError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmText))]
    private string? _installWarning;

    [ObservableProperty]
    private InstallChoices? _choices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmText))]
    private InstallPlan? _pendingPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    [NotifyPropertyChangedFor(nameof(ChangeTexts))]
    private ModPackUpdateResult? _pendingResult;

    internal ModPackUpdateRequest? PendingRequest { get; set; }

    /// <summary>The warnings about the pack itself, without those of <see cref="PendingPlan"/>.</summary>
    internal IReadOnlyList<string> PendingReasons { get; set; } = [];

    public bool IsConfirming => PendingResult is not null;

    /// <summary>What the confirmation lists: the mods the update adds, changes, removes and keeps.</summary>
    public IReadOnlyList<string> ChangeTexts => PendingResult is not { } result ? []
        : result.Changes.Count == 0 ? [_owner.Localization.PackUpdateNoModChanges]
        : result.Changes.Select(ChangeText).ToList();

    /// <summary>"Update pack" or "Update anyway", with the download size when the index states one.</summary>
    public string ConfirmText
    {
        get
        {
            var text = InstallWarning is null ? _owner.Localization.PackUpdate : _owner.Localization.UpdateAnyway;
            return MainViewModel.PlanSizeText(PendingPlan) is { } size ? $"{text} ({size})" : text;
        }
    }

    public PackUpdateItem(MainViewModel owner, Guid instanceId, ModPackResult pack)
    {
        _owner = owner;
        InstanceId = instanceId;
        Pack = pack;
    }

    private string ChangeText(ModPackChange change)
    {
        var name = _owner.ContentName(change.ModId);
        return change.Kind switch
        {
            ModPackChangeKind.Add => _owner.Localization.FormatPackUpdateAdd(name, change.To!.Value.ToString()),
            ModPackChangeKind.Change => _owner.Localization.FormatPackUpdateChange(name, change.From!.Value.ToString(), change.To!.Value.ToString()),
            ModPackChangeKind.Remove => _owner.Localization.FormatPackUpdateRemove(name, change.From!.Value.ToString()),
            _ when change.To != change.From => _owner.Localization.FormatPackUpdateKeepChange(name, change.From!.Value.ToString(), change.To!.Value.ToString()),
            _ => _owner.Localization.FormatPackUpdateKeep(name, change.From!.Value.ToString()),
        };
    }

    internal void ClearOutcome()
    {
        InstallError = null;
        ProgressStatus = null;
        CancelUpdate();
    }

    /// <param name="stopped">What the notice shows after a stop, or null.</param>
    internal void EndInstall(string? stopped = null)
    {
        IsInstalling = false;
        Run = null;
        Progress = 0;
        ProgressStatus = stopped;
        ProgressDetail = null;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(NoticeText));
        OnPropertyChanged(nameof(ChangeTexts));
        OnPropertyChanged(nameof(ConfirmText));
    }

    [RelayCommand]
    private Task UpdateAsync() => _owner.UpdatePackAsync(this);

    [RelayCommand]
    private Task ConfirmUpdateAsync() => _owner.ConfirmPackUpdateAsync(this);

    [RelayCommand]
    internal void CancelUpdate()
    {
        PendingRequest = null;
        PendingResult = null;
        PendingReasons = [];
        PendingPlan = null;
        InstallWarning = null;
        Choices = null;
    }
}
