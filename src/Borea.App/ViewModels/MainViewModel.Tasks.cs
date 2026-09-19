using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.History;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>
/// The Tasks page: what runs now, what Borea did, and Try again for a failed task.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The exact version each install row planned last, so the task of its confirmation keeps that request.</summary>
    private readonly ConditionalWeakTable<IInstallRow, string> _requestedVersions = new();

    public TaskRegistry Tasks { get; }

    private TaskItem StartTask(TaskKind kind, string? subject = null, Guid? instanceId = null, string? contentId = null, string? version = null, TaskState state = TaskState.Running)
        => Tasks.Start(kind, subject, instanceId, Instances.FirstOrDefault(instance => instance.InstanceId == instanceId)?.Name, contentId, version, state);

    private void RememberRequestedVersion(IInstallRow row, ModVersion? version)
    {
        if (version is { } exact)
            _requestedVersions.AddOrUpdate(row, exact.ToString());
        else
            _requestedVersions.Remove(row);
    }

    /// <summary>The task of an install or update that a row plans. It waits until the plan runs.</summary>
    private TaskItem StartInstallTask(IInstallRow row, Guid? instanceId) => row switch
    {
        ContentItem item => StartTask(TaskKind.Update, item.Name, instanceId, item.ModId, state: TaskState.Waiting),
        UpdateAllItem => StartTask(TaskKind.UpdateAll, instanceId: instanceId, state: TaskState.Waiting),
        VersionItem item => StartModInstallTask(item, item.ModId, instanceId),
        DiscoverItem item => StartModInstallTask(item, item.ModId, instanceId),
        _ => throw new UnreachableException(),
    };

    private TaskItem StartModInstallTask(IInstallRow row, string modId, Guid? instanceId)
    {
        var version = _requestedVersions.TryGetValue(row, out var requested) ? requested : null;
        var name = ContentName(modId);
        return StartTask(TaskKind.ModInstall, version is null ? name : $"{name} {version}", instanceId, modId, version, TaskState.Waiting);
    }

    private TaskItem StartPackInstallTask(PackItem pack, Guid? instanceId)
    {
        var version = pack.RequestedVersion?.ToString();
        return StartTask(TaskKind.PackInstall, version is null ? pack.Name : $"{pack.Name} {version}", instanceId, pack.PackId, version, TaskState.Waiting);
    }

    /// <summary>A task that neither completed, failed nor stopped only planned, so it leaves no history.</summary>
    private void EndTask(TaskItem task, bool completed, bool stopped, string? error)
    {
        if (stopped)
            Tasks.End(task, TaskState.Stopped);
        else if (error is not null)
            Tasks.End(task, TaskState.Failed, error);
        else if (completed)
            Tasks.End(task, TaskState.Finished);
        else
            Tasks.Discard(task);
    }

    /// <summary>
    /// Plans the request of a failed task again. An install or an update opens
    /// the page of its row first, because a plan that asks something waits there.
    /// </summary>
    private async Task RetryTaskAsync(TaskItem task)
    {
        if (_services is not { } services || Tasks.IsBusyWith(task))
            return;

        var instance = Instances.FirstOrDefault(item => item.InstanceId == task.InstanceId);
        if (task.InstanceId is not null && instance is null)
        {
            FailRetry(task, Localization.LaunchInstanceMissing);
            return;
        }

        switch (task.Kind)
        {
            case TaskKind.IndexRefresh:
                await RetryContentIndexAsync();
                break;
            case TaskKind.ModInstall when instance is not null:
                await RetryInstallAsync(services, task, instance);
                break;
            case TaskKind.PackInstall when instance is not null:
                await RetryPackInstallAsync(task, instance);
                break;
            case TaskKind.Update or TaskKind.UpdateAll when instance is not null:
                await RetryUpdateAsync(task, instance);
                break;
            case TaskKind.PackUpdate when instance is not null:
                await RetryPackUpdateAsync(task, instance);
                break;
        }
    }

    private async Task RetryInstallAsync(BoreaServices services, TaskItem task, InstanceItem instance)
    {
        await EnsureDiscoverLoadedAsync();
        ModVersion? version = ModVersion.TryParse(task.Version, out var exact) ? exact : null;
        if (task.ContentId is not { } modId
            || (task.Version is not null && version is null)
            || _listings.FirstOrDefault(listing => ModIds.Equals(listing.ModId, modId)) is not { } row)
        {
            FailRetry(task, Localization.DiscoverNoRelease);
            return;
        }

        await OpenContentAsync(row);
        await PlanInstallAsync(row, () => version is { } pinned ? services.Mods.GetReleaseAsync(modId, pinned) : services.Mods.GetLatestReleaseAsync(modId), version, instance.InstanceId);
    }

    private async Task RetryPackInstallAsync(TaskItem task, InstanceItem instance)
    {
        await EnsureDiscoverLoadedAsync();
        ModVersion? version = ModVersion.TryParse(task.Version, out var exact) ? exact : null;
        if ((task.Version is not null && version is null)
            || _packs.FirstOrDefault(pack => ModIds.Equals(pack.PackId, task.ContentId)) is not { } pack)
        {
            FailRetry(task, Localization.DiscoverNoRelease);
            return;
        }

        await OpenPackAsync(pack);
        await InstallPackAsync(pack, instance.InstanceId, version);
    }

    private async Task RetryUpdateAsync(TaskItem task, InstanceItem instance)
    {
        await OpenInstanceAsync(instance);
        if (task.Kind == TaskKind.UpdateAll)
        {
            if (UpdateAll is { } all)
                await UpdateAllContentAsync(all);
        }
        else if (_content.FirstOrDefault(content => ModIds.Equals(content.ModId, task.ContentId)) is { } row)
        {
            await UpdateContentAsync(row);
        }
        else
        {
            FailRetry(task, Localization.TaskRetryModMissing);
        }
    }

    private async Task RetryPackUpdateAsync(TaskItem task, InstanceItem instance)
    {
        await OpenInstanceAsync(instance);
        if (PackUpdate is { } update)
            await UpdatePackAsync(update);
        else
            FailRetry(task, Localization.PackUpdateNotNewer);
    }

    private void FailRetry(TaskItem task, string reason)
        => Tasks.End(Tasks.Start(task.Kind, task.Subject, task.InstanceId, task.InstanceName, task.ContentId, task.Version, TaskState.Running), TaskState.Failed, reason);
}
