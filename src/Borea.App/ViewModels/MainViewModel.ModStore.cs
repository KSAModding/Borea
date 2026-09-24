using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// The shared mod store in the General settings, and the check that gives each
/// instance its own copy of a stored release that a mod changed. The check runs
/// when an instance page opens and after the game of a launch closes.
/// </summary>
public partial class MainViewModel
{
    private Task _sharedModStoreSave = Task.CompletedTask;

    private Task _modStoreCheck = Task.CompletedTask;

    private Task _gameExitWatch = Task.CompletedTask;

    [ObservableProperty]
    private bool _isChangingSharedModStore;

    [ObservableProperty]
    private string? _sharedModStoreError;

    /// <summary>
    /// Turning it off gives every instance its own copy of each linked mod, so
    /// it waits for the running tasks, like a change of the library folder.
    /// </summary>
    public bool UseSharedModStore
    {
        get => _services?.Settings.SharedModStore ?? true;
        set
        {
            if (_services is null || value == UseSharedModStore || IsSetupBusy || IsChangingLibraryFolder)
            {
                OnPropertyChanged(nameof(UseSharedModStore));
                return;
            }

            _sharedModStoreSave = SaveSharedModStoreAsync(_services, value);
        }
    }

    /// <summary>Completes when the last change of the shared mod store is saved.</summary>
    internal Task WhenSharedModStoreSavedAsync() => _sharedModStoreSave;

    /// <summary>Completes when the checks started so far are done.</summary>
    internal Task WhenModStoreCheckedAsync() => _modStoreCheck;

    /// <summary>Completes when the games launched so far are closed and checked.</summary>
    internal Task WhenGameExitCheckedAsync() => _gameExitWatch;

    private async Task SaveSharedModStoreAsync(BoreaServices services, bool enabled)
    {
        SharedModStoreError = null;
        if (Volatile.Read(ref _libraryUses) > 0)
        {
            SharedModStoreError = Localization.SharedModStoreWaitForTask;
            OnPropertyChanged(nameof(UseSharedModStore));
            return;
        }

        IsSetupBusy = true;
        IsChangingSharedModStore = !enabled;
        try
        {
            SharedModStoreChange change;
            try
            {
                change = await Task.Run(() => services.SharedModStore.SetEnabledAsync(enabled));
            }
            catch (Exception exception) when (!enabled && IsSharedModStoreFailure(exception))
            {
                // the setting is saved before the break-out, so new installs must stop linking now
                await RebuildServicesAsync();
                SharedModStoreError = UseSharedModStore
                    ? Localization.FormatSharedModStoreFailed(exception.Message)
                    : Localization.FormatSharedModStoreBreakOutFailed(exception.Message);
                return;
            }

            if (change == SharedModStoreChange.Saved)
                await RebuildServicesAsync();
            else
                SharedModStoreError = change == SharedModStoreChange.GameRunning ? Localization.SharedModStoreGameRunning : Localization.SharedModStoreBoreaRunning;
        }
        catch (Exception exception) when (IsSharedModStoreFailure(exception))
        {
            SharedModStoreError = Localization.FormatSharedModStoreFailed(exception.Message);
        }
        finally
        {
            IsSetupBusy = false;
            IsChangingSharedModStore = false;
            OnPropertyChanged(nameof(UseSharedModStore));
        }
    }

    private static bool IsSharedModStoreFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or HttpRequestException;

    private void StartModStoreCheck(Guid instanceId) => _modStoreCheck = Chain(_modStoreCheck, CheckModStoreAsync(instanceId));

    /// <summary>
    /// The wait lasts as long as the game runs, so it is kept apart from the
    /// checks that a change of the library folder waits for.
    /// </summary>
    private void StartModStoreCheckAfterGame(Guid instanceId)
    {
        if (_services is not { } services)
            return;

        _gameExitWatch = Chain(_gameExitWatch, WatchAsync());

        async Task WatchAsync()
        {
            await services.SharedModStore.WaitForGameExitAsync(instanceId);
            StartModStoreCheck(instanceId);
            await _modStoreCheck;
        }
    }

    private static Task Chain(Task previous, Task next) => previous.IsCompleted ? next : Task.WhenAll(previous, next);

    private async Task CheckModStoreAsync(Guid instanceId)
    {
        using var libraryUse = TryUseLibrary();
        if (_services is not { } services || libraryUse is null)
            return;

        IReadOnlyList<InstalledMod> brokenOut;
        try
        {
            brokenOut = await Task.Run(() => services.SharedModStore.CheckAsync(instanceId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            services.Log.Write($"Instance {instanceId}: Borea could not check the shared mod store. {exception.Message}");
            return;
        }

        if (brokenOut.Count == 0)
            return;

        foreach (var mod in brokenOut)
        {
            var name = mod.Metadata.Listing?.Name ?? mod.ModId;
            services.Log.Write($"Instance {instanceId}: {mod.ModId} {mod.Version} changed its files in the shared mod store, so every instance that used it now has its own copy.");
            ShowSuccessToast(() => Localization.FormatSharedModStoreBrokenOut(name));
        }

        await ReloadInstancesAsync();
    }
}
