using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>
/// The toast about a game that closed unexpectedly after its start, shown when
/// the process of a launch ends with a crash the game recorded, and once at the
/// start of Borea for a crash of the active instance after its last launch. An
/// error of the mod loader after the game shut down gets no toast.
/// </summary>
public partial class MainViewModel
{
    private Task _crashCheck = Task.CompletedTask;

    private bool _crashChecked;

    /// <summary>Completes when the check for a crash since the last launch is done.</summary>
    internal Task WhenCrashCheckedAsync() => _crashCheck;

    /// <summary>The wait lasts as long as the game runs, so it joins the other waits for the end of a game.</summary>
    private void StartCrashWatch(Instance instance, LaunchResult started)
    {
        if (_services is not { } services)
            return;

        _gameExitWatch = Chain(_gameExitWatch, WatchAsync());

        async Task WatchAsync()
        {
            try
            {
                var exit = await services.Launcher.WatchExitAsync(instance, started);
                if (exit.Crash is { } crash)
                    await ShowCrashAsync(services, instance, crash);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
            {
                // nobody awaits this watch, so a fault would otherwise vanish, and the start check still shows the crash later
                services.Log.Write($"Borea could not watch the game of instance {instance.InstanceId} for a crash. {exception.Message}");
            }
        }
    }

    private void StartCrashCheck()
    {
        if (_services is not { } services || _crashChecked)
            return;

        _crashChecked = true;
        _crashCheck = CheckCrashAsync(services);
    }

    private async Task CheckCrashAsync(BoreaServices services)
    {
        try
        {
            if (await services.Instances.GetActiveInstanceIdAsync() is not { } instanceId
                || await services.Instances.GetByIdAsync(instanceId) is not { LastPlayedAt: { } lastLaunch } instance)
            {
                return;
            }

            if (await services.GameCrashes.GetUnreportedAsync(instance, lastLaunch) is { } crash)
            {
                services.Log.Write($"Instance {instanceId}: the game crashed after its last launch. Crash log: {crash.LogPath}. {crash.Exception ?? "No exception in the log."}");
                await ShowCrashAsync(services, instance, crash);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            services.Log.Write($"Borea could not check the active instance for a crash of the game. {exception.Message}");
        }
    }

    private async Task ShowCrashAsync(BoreaServices services, Instance instance, GameCrash crash)
    {
        // the player's session ended normally, so the error of the loader on its way out goes to the log and not to a toast
        if (crash.AfterShutdown)
        {
            services.Log.Write($"Instance {instance.InstanceId}: the mod loader failed after the game shut down. Crash log: {crash.LogPath}. {crash.Exception ?? "No exception in the log."}");
            await services.GameCrashes.MarkReportedAsync(instance.InstanceId, crash);
            return;
        }

        var blamed = crash.BlamedModId is null ? null : instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, crash.BlamedModId));
        var name = blamed is null ? null : blamed.Metadata.Listing?.Name ?? (await ResolveListingAsync(blamed.ModId))?.Name ?? blamed.ModId;
        Toasts.ShowMessage(ToastKind.Error, () => name is null ? Localization.LaunchGameCrashed : Localization.FormatLaunchGameCrashedBy(name), crash.Exception, new ToastAction(() => Localization.GameLogOpen, () => OpenLogFromToast(crash.LogPath)));
        await services.GameCrashes.MarkReportedAsync(instance.InstanceId, crash);
    }

    internal void OpenLogFromToast(string path) => ShowOpenError(() => PathName(path), TryOpenWithSystem(path));
}
