using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.App.ViewModels;

/// <summary>
/// A row that installs a release: a Discover row, or a row of the versions table.
/// </summary>
internal interface IInstallRow
{
    bool IsInstalling { get; set; }

    double Progress { get; set; }

    /// <summary>What the install is doing, for example "Downloading MeasureTools 1.1.10 (2 of 3)".</summary>
    string? ProgressStatus { get; set; }

    /// <summary>Size and time left while downloading, otherwise null.</summary>
    string? ProgressDetail { get; set; }

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
        if (_services is null || ActiveInstance is null || row.IsInstalling)
            return;

        var services = _services;
        var instanceId = ActiveInstance.InstanceId;
        row.InstallError = null;
        row.InstallWarning = null;
        row.PendingPlan = null;
        row.IsInstalling = true;
        var executed = false;
        try
        {
            var release = await findRelease() ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
            var instance = await services.Instances.GetByIdAsync(instanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
            var request = new InstallPlanningRequest(
                instance,
                [new RequestedMod(release, InstallReason.Manual, exact)],
                services.Mods,
                services.InstalledVersion.GetInstalledVersion()?.Version,
                CurrentPlatform());
            var plan = await services.InstallPlanner.PlanAsync(request);

            if (!plan.IsReady)
            {
                row.InstallError = Describe(plan.Conflicts.Concat(plan.UnresolvedChoices));
            }
            else if (plan.Warnings.Count > 0)
            {
                row.PendingPlan = plan;
                row.InstallWarning = Describe(plan.Warnings);
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

        if (executed)
            await ReloadInstancesAsync();
    }

    /// <summary>
    /// Runs the plan the row holds after the user accepted its warnings. The
    /// executor refuses it when the instance changed since it was planned.
    /// </summary>
    internal async Task ConfirmInstallAsync(IInstallRow row)
    {
        if (_services is null || row.PendingPlan is not { } plan || row.IsInstalling)
            return;

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

        await ReloadInstancesAsync();
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
    private IProgress<InstallProgress> ProgressOf(IInstallRow row)
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
    private static string Describe(IEnumerable<PlanningMessage> messages)
        => string.Join(" ", messages.Select(message => $"{message.ModId}: {message.Message}"));

    private static OsPlatform CurrentPlatform()
        => OperatingSystem.IsWindows() ? OsPlatform.Windows : OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs;
}
