using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.App.ViewModels;

internal interface IInstallProgressRow
{
    bool IsInstalling { get; set; }

    double Progress { get; set; }

    /// <summary>What the install is doing, for example "Downloading MeasureTools 1.1.10 (2 of 3)".</summary>
    string? ProgressStatus { get; set; }

    /// <summary>Size and time left while downloading, otherwise null.</summary>
    string? ProgressDetail { get; set; }
}

/// <summary>
/// A row that installs a release: a Discover row, a row of the versions table,
/// or an update on the instance page.
/// </summary>
internal interface IInstallRow : IInstallProgressRow
{
    string? InstallError { get; set; }

    string? InstallWarning { get; set; }

    InstallPlan? PendingPlan { get; set; }
}

/// <summary>
/// Installs go through the planner and the executor the CLI uses. The planner
/// adds required dependencies and blocks an incompatible release, and the
/// executor writes only while the instance still matches the plan.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Plans the install of one release into the active instance. A ready plan
    /// without warnings runs at once. A plan with warnings, such as an untested
    /// game version, waits on the row until the user confirms or cancels it.
    /// </summary>
    internal async Task PlanInstallAsync(IInstallRow row, Func<Task<ModVersionMetadata?>> findRelease, bool exact)
    {
        if (_services is null || ActiveInstance is null)
            return;

        var executed = await PlanAndExecuteAsync(row, ActiveInstance.InstanceId, async _ =>
        {
            var release = await findRelease() ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
            return [new RequestedMod(release, InstallReason.Manual, exact)];
        });

        if (executed)
            await ReloadInstancesAsync();
    }

    /// <summary>
    /// Plans the requested mods into the instance and runs a ready plan without
    /// warnings, unless <paramref name="waitForConfirmation"/> holds it. Returns
    /// whether the executor ran, so the caller reloads the instances.
    /// </summary>
    private async Task<bool> PlanAndExecuteAsync(IInstallRow row, Guid instanceId, Func<Instance, Task<IReadOnlyList<RequestedMod>>> requestMods, Func<Instance, InstallPlan, Task<bool>>? waitForConfirmation = null)
    {
        if (_services is null || row.IsInstalling)
            return false;

        var services = _services;
        row.InstallError = null;
        row.InstallWarning = null;
        row.PendingPlan = null;
        row.IsInstalling = true;
        var executed = false;
        try
        {
            var instance = await services.Instances.GetByIdAsync(instanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
            var plan = await services.InstallPlanner.PlanAsync(PlanningRequest(services, instance, await requestMods(instance)));
            var wait = plan.IsReady && waitForConfirmation is not null && await waitForConfirmation(instance, plan);

            if (!plan.IsReady)
            {
                row.InstallError = Describe(plan, plan.Conflicts.Concat(plan.UnresolvedChoices));
            }
            else if (plan.Warnings.Count > 0 || wait)
            {
                row.PendingPlan = plan;
                row.InstallWarning = plan.Warnings.Count > 0 ? Describe(plan, plan.Warnings) : null;
            }
            else
            {
                executed = true;
                await services.PlanExecutor.ExecuteAsync(plan, enable: true, ProgressOf(row));
            }
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            row.InstallError = exception.Message;
        }
        finally
        {
            row.IsInstalling = false;
            row.Progress = 0;
            row.ProgressStatus = null;
            row.ProgressDetail = null;
        }

        return executed;
    }

    private static InstallPlanningRequest PlanningRequest(BoreaServices services, Instance instance, IReadOnlyList<RequestedMod> requested)
        => new(instance, requested, services.Mods, services.InstalledVersion.GetInstalledVersion()?.Version, CurrentPlatform());

    /// <summary>
    /// Runs the plan the row holds after the user accepted its warnings. The
    /// executor refuses it when the instance changed since it was planned.
    /// </summary>
    internal async Task ConfirmInstallAsync(IInstallRow row)
    {
        if (await ExecutePendingPlanAsync(row))
            await ReloadInstancesAsync();
    }

    private async Task<bool> ExecutePendingPlanAsync(IInstallRow row)
    {
        if (_services is null || row.PendingPlan is not { } plan || row.IsInstalling)
            return false;

        row.PendingPlan = null;
        row.InstallWarning = null;
        row.IsInstalling = true;
        try
        {
            await _services.PlanExecutor.ExecuteAsync(plan, enable: true, ProgressOf(row));
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            row.InstallError = exception.Message;
        }
        finally
        {
            row.IsInstalling = false;
            row.Progress = 0;
            row.ProgressStatus = null;
            row.ProgressDetail = null;
        }

        return true;
    }

    internal static void CancelInstall(IInstallRow row)
    {
        row.PendingPlan = null;
        row.InstallWarning = null;
    }

    /// <summary>
    /// Reports land on the UI thread through <see cref="Progress{T}"/>, and
    /// each install gets its own text so its download rate starts fresh.
    /// </summary>
    private IProgress<InstallProgress> ProgressOf(IInstallProgressRow row)
    {
        var text = new InstallProgressText(Localization);
        return new Progress<InstallProgress>(value =>
        {
            if (!row.IsInstalling)
                return;

            text.Report(value);
            row.Progress = text.Percent;
            row.ProgressStatus = text.Status;
            row.ProgressDetail = text.Detail;
        });
    }

    private static bool IsInstallFailure(Exception exception)
        => exception is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException
            or DownloadFailedException or NotSupportedException or TaskCanceledException or ModReplacementRecoveryException;

    /// <summary>
    /// The planner's messages on one line, each named by its mod.
    /// </summary>
    private string Describe(InstallPlan plan, IEnumerable<PlanningMessage> messages)
        => string.Join(" ", messages.Select(message => $"{message.ModId}: {Translate(plan, message)}"));

    /// <summary>
    /// The channel warning in the display language. The other messages stay
    /// as the planner wrote them.
    /// </summary>
    private string Translate(InstallPlan plan, PlanningMessage message)
    {
        if (message.Code != "release-channel")
            return message.Message;

        var release = plan.Operations.FirstOrDefault(operation => ModIds.Equals(operation.Release.ModId, message.ModId))?.Release;
        return release is null
            ? message.Message
            : Localization.FormatInstallReleaseChannel(release.Version.ToString(), ReleaseStatusText(release.ReleaseStatus));
    }

    private static OsPlatform CurrentPlatform()
        => OperatingSystem.IsWindows() ? OsPlatform.Windows : OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs;
}
