using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// "Keep this game build" in the instance menu and "Keep a build from a folder" in the Game settings: a modal that
/// says what the copy needs, and the copy as a task in the Tasks drawer. A new instance can then pick a kept build.
/// "Use the current game build" moves the instance back to the game folder of the settings, and Settings, Game lists
/// the kept builds and removes the ones no instance uses.
/// </summary>
public partial class MainViewModel
{
    private InstanceItem? _keepGameBuildItem;

    private string? _keepGameBuildFolder;

    /// <summary>What keeping the build would do for the row or the folder of the open modal. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeepGameBuildModalOpen), nameof(KeepGameBuildText), nameof(KeepGameBuildFreeSpaceText), nameof(KeepGameBuildRefusalText), nameof(CanConfirmKeepGameBuild))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmKeepGameBuildCommand))]
    private GameBuildKeepCheck? _keepGameBuildCheck;

    public bool IsKeepGameBuildModalOpen => KeepGameBuildCheck is not null;

    public bool CanConfirmKeepGameBuild => KeepGameBuildCheck?.Refusal == GameBuildKeepRefusal.None;

    public string? KeepGameBuildText
    {
        get
        {
            if (KeepGameBuildCheck is not { Refusal: GameBuildKeepRefusal.None, Version: { } version, Folder: { } folder } check)
                return null;

            if (_keepGameBuildFolder is { } source)
            {
                return check.BytesToCopy > 0
                    ? Localization.FormatModalAddGameBuildText(version.ToString(), SizeText(check.BytesToCopy), source, folder)
                    : Localization.FormatModalAddGameBuildShared(version.ToString(), folder);
            }

            if (_keepGameBuildItem is { } item)
            {
                return check.BytesToCopy > 0
                    ? Localization.FormatModalKeepGameBuildText(version.ToString(), SizeText(check.BytesToCopy), folder, item.Name)
                    : Localization.FormatModalKeepGameBuildShared(version.ToString(), folder, item.Name);
            }

            return null;
        }
    }

    public string? KeepGameBuildFreeSpaceText => KeepGameBuildCheck is { Refusal: GameBuildKeepRefusal.None, BytesToCopy: > 0, FreeBytes: { } free }
        ? Localization.FormatModalKeepGameBuildFreeSpace(SizeText(free))
        : null;

    public string? KeepGameBuildRefusalText => KeepGameBuildCheck is { } check ? KeepGameBuildRefusal(check, _keepGameBuildFolder is not null) : null;

    internal async Task BeginKeepGameBuildAsync(InstanceItem item)
    {
        if (_services is not { } services)
            return;

        GameBuildKeepCheck check;
        try
        {
            check = await Task.Run(() => services.GameBuilds.Check(item.Instance));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowErrorToast(() => Localization.InstanceKeepGameBuild, exception.Message);
            return;
        }

        _keepGameBuildFolder = null;
        _keepGameBuildItem = item;
        KeepGameBuildCheck = check;
    }

    /// <summary>Why Borea could not read the folder the player chose in the Game settings, or null.</summary>
    [ObservableProperty]
    private string? _addGameBuildError;

    /// <summary>Opens the modal for the build in <paramref name="folder"/>, which the player chose in the Game settings.</summary>
    internal async Task BeginAddGameBuildAsync(string folder)
    {
        if (_services is not { } services)
            return;

        AddGameBuildError = null;
        GameBuildKeepCheck check;
        try
        {
            check = await Task.Run(() => services.GameBuilds.CheckFolder(folder));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a toast would be under the Settings modal, so the reason shows under the button
            AddGameBuildError = exception.Message;
            services.Log.Write($"Game build folder {folder}: {exception.Message}");
            return;
        }

        _keepGameBuildItem = null;
        _keepGameBuildFolder = folder;
        KeepGameBuildCheck = check;
    }

    [RelayCommand]
    private void CancelKeepGameBuild()
    {
        KeepGameBuildCheck = null;
        _keepGameBuildItem = null;
        _keepGameBuildFolder = null;
    }

    // a keep for another instance can start while a copy runs, and the keeper runs the keeps one at a time
    [RelayCommand(CanExecute = nameof(CanConfirmKeepGameBuild), AllowConcurrentExecutions = true)]
    private async Task ConfirmKeepGameBuildAsync()
    {
        var item = _keepGameBuildItem;
        var folder = _keepGameBuildFolder;
        if (_services is not { } services || (item is null && folder is null) || KeepGameBuildCheck is not { Refusal: GameBuildKeepRefusal.None, Version: { } version })
            return;

        CancelKeepGameBuild();

        // the Settings modal would cover the drawer row with its Stop and the toast at the end
        if (folder is not null)
            IsSettingsOpen = false;

        var build = version.ToString();
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastGameBuildKeepFailed(build), Localization.LibraryFolderBusy);
            return;
        }

        var run = StartInstallRun(StartTask(TaskKind.GameBuildKeep, build, item?.InstanceId));
        var task = run.TaskItem;
        if (folder is not null)
            ShowTaskDetails(task);

        var progress = new Progress<LibraryMoveProgress>(value => run.ShowReport(() => task.Report(
            Localization.FormatLibraryFolderCopying(value.Files, value.TotalFiles, InstallProgressText.Number(value.Bytes), InstallProgressText.Number(value.TotalBytes)),
            value.PercentComplete)));
        var completed = false;
        var stopped = false;
        string? error = null;
        var source = item is not null ? $"Instance {item.InstanceId}" : $"Game build folder {folder}";
        var logText = $"{source}: kept KSA {build}.";
        try
        {
            var result = await Task.Run(() => item is not null
                ? services.GameBuilds.KeepAsync(item.InstanceId, version, progress, run.InstallStop.Token)
                : services.GameBuilds.KeepFolderAsync(folder!, version, progress, run.InstallStop.Token));

            // a second keep of the same instance, started while the row did not show the first one yet, finds it done
            completed = result.Refusal == GameBuildKeepRefusal.None
                || (result.Refusal == GameBuildKeepRefusal.AlreadyKept && result.Version == version);
            if (!completed)
            {
                error = KeepGameBuildRefusal(result, folder is not null);
                logText = $"{source}: KSA {build} was not kept, refused as {result.Refusal}.";
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            logText = $"{source}: the copy of KSA {build} was stopped.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = exception.Message;
            logText = $"{source}: KSA {build} was not kept. {exception.Message}";
        }
        finally
        {
            EndInstallRun(run, completed, stopped, error);
        }

        services.Log.Write(logText);
        await ReloadInstancesAsync();
        RefreshKeptGameBuildsIfShown();
    }

    private string? KeepGameBuildRefusal(GameBuildKeepCheck check, bool fromFolder) => check.Refusal switch
    {
        GameBuildKeepRefusal.AlreadyKept => Localization.FormatKeepGameBuildAlreadyKept(check.Version?.ToString() ?? string.Empty),
        GameBuildKeepRefusal.NoGame => Localization.KeepGameBuildNoGame,
        GameBuildKeepRefusal.WineWrapper => Localization.KeepGameBuildWineWrapper,
        GameBuildKeepRefusal.GameRunning => Localization.GameSaveCloseGame,
        GameBuildKeepRefusal.FolderTaken => Localization.FormatKeepGameBuildFolderTaken(check.Folder ?? string.Empty),
        GameBuildKeepRefusal.LibraryInsideGame => Localization.KeepGameBuildLibraryInsideGame,
        GameBuildKeepRefusal.NotEnoughSpace => Localization.FormatKeepGameBuildNotEnoughSpace(SizeText(check.BytesToCopy), SizeText(check.FreeBytes ?? 0)),
        GameBuildKeepRefusal.BuildChanged when fromFolder => Localization.FormatKeepGameBuildFolderChanged(check.Version?.ToString() ?? string.Empty),
        GameBuildKeepRefusal.BuildChanged => Localization.FormatKeepGameBuildBuildChanged(check.Version?.ToString() ?? string.Empty),
        GameBuildKeepRefusal.NotAGame => Localization.KeepGameBuildNotAGame,
        _ => null,
    };

    /// <summary>The game build picker of the new instance modal, with the game folder of the settings first.</summary>
    public ObservableCollection<GameBuildChoice> GameBuildChoices { get; } = [];

    [ObservableProperty]
    private GameBuildChoice? _selectedGameBuildChoice;

    /// <summary>
    /// The picker belongs to a plain new instance, the same as the game settings preset, and shows only once a build
    /// is kept.
    /// </summary>
    public bool CanPickGameBuild => CanPickGameSettingsPreset && GameBuildChoices.Count > 1;

    private Task _gameBuildChoicesLoad = Task.CompletedTask;

    /// <summary>Counts the loads, so that a load which a newer one started after drops its result.</summary>
    private int _gameBuildChoicesLoadCount;

    internal Task WhenGameBuildChoicesLoadedAsync() => _gameBuildChoicesLoad;

    /// <summary>Starts a read of the kept builds without awaiting it. The picker starts at the game folder of the settings again.</summary>
    private void StartGameBuildChoicesLoad()
    {
        var settings = new GameBuildChoice(null, Localization.ModalGameBuildSettings);
        GameBuildChoices.Clear();
        GameBuildChoices.Add(settings);
        SelectedGameBuildChoice = settings;
        OnPropertyChanged(nameof(CanPickGameBuild));
        _gameBuildChoicesLoad = LoadGameBuildChoicesAsync(++_gameBuildChoicesLoadCount);
    }

    private async Task LoadGameBuildChoicesAsync(int load)
    {
        if (_services is not { } services)
            return;

        IReadOnlyList<GameVersion> kept;
        try
        {
            kept = await Task.Run(services.GameBuilds.ListKept);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the modal still creates an instance on the build of the settings
            return;
        }

        if (load != _gameBuildChoicesLoadCount)
            return;

        foreach (var build in kept)
            GameBuildChoices.Add(new GameBuildChoice(build, Localization.FormatInstanceGameBuild(build.ToString())));

        OnPropertyChanged(nameof(CanPickGameBuild));
    }

    private void RefreshKeepGameBuildText()
    {
        OnPropertyChanged(nameof(KeepGameBuildText));
        OnPropertyChanged(nameof(KeepGameBuildFreeSpaceText));
        OnPropertyChanged(nameof(KeepGameBuildRefusalText));
        OnPropertyChanged(nameof(UseCurrentGameBuildText));
        OnPropertyChanged(nameof(UseCurrentGameBuildFitText));
        OnPropertyChanged(nameof(UseCurrentGameBuildIncompatibleText));
        OnPropertyChanged(nameof(UseCurrentGameBuildUnknownText));
        OnPropertyChanged(nameof(UseCurrentGameBuildOlderText));
        OnPropertyChanged(nameof(UseCurrentGameBuildNoGameText));
        foreach (var build in KeptGameBuilds)
            build.RefreshText();
    }

    private InstanceItem? _useCurrentGameBuildItem;

    /// <summary>How the mods of the row of the open modal fit the build of the settings. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUseCurrentGameBuildModalOpen), nameof(UseCurrentGameBuildText), nameof(UseCurrentGameBuildFitText), nameof(UseCurrentGameBuildIncompatibleText), nameof(UseCurrentGameBuildUnknownText), nameof(ShowsUseCurrentGameBuildModNames), nameof(UseCurrentGameBuildOlderText), nameof(UseCurrentGameBuildNoGameText), nameof(CanConfirmUseCurrentGameBuild))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmUseCurrentGameBuildCommand))]
    private UseCurrentGameBuildPreview? _useCurrentGameBuildPreview;

    public bool IsUseCurrentGameBuildModalOpen => UseCurrentGameBuildPreview is not null;

    public bool CanConfirmUseCurrentGameBuild => UseCurrentGameBuildPreview?.Current is not null;

    public string? UseCurrentGameBuildText => UseCurrentGameBuildPreview is { Current: { } current } preview && _useCurrentGameBuildItem is { } item
        ? Localization.FormatModalUseCurrentGameBuildText(item.Name, current.ToString(), preview.Kept.ToString())
        : null;

    public string? UseCurrentGameBuildFitText => UseCurrentGameBuildPreview is { Current: { } current, Total: > 0 } preview
        ? Localization.FormatModalUseCurrentGameBuildFit(preview.Total - preview.Incompatible.Count - preview.Unknown.Count, preview.Total, current.ToString())
        : null;

    public string? UseCurrentGameBuildIncompatibleText => UseCurrentGameBuildPreview is { Current: not null, Incompatible.Count: > 0 } preview
        ? Localization.FormatModalUseCurrentGameBuildIncompatible(string.Join(", ", preview.Incompatible))
        : null;

    public string? UseCurrentGameBuildUnknownText => UseCurrentGameBuildPreview is { Current: not null, Unknown.Count: > 0 } preview
        ? Localization.FormatModalUseCurrentGameBuildUnknown(string.Join(", ", preview.Unknown))
        : null;

    /// <summary>The two lists of mod names scroll together, so a long list does not push the buttons out of the window.</summary>
    public bool ShowsUseCurrentGameBuildModNames => UseCurrentGameBuildPreview is { Current: not null } preview
        && (preview.Incompatible.Count > 0 || preview.Unknown.Count > 0);

    // SaveMetaData.Write stores the build in each save, but UncompressedSave.Load never compares it, so the game itself does not warn
    public string? UseCurrentGameBuildOlderText => UseCurrentGameBuildPreview is { Current: { } current } preview && current < preview.Kept
        ? Localization.FormatModalUseCurrentGameBuildOlder(current.ToString(), preview.Kept.ToString())
        : null;

    public string? UseCurrentGameBuildNoGameText => UseCurrentGameBuildPreview is { Current: null } ? Localization.KeepGameBuildNoGame : null;

    internal async Task BeginUseCurrentGameBuildAsync(InstanceItem item)
    {
        if (_services is not { } services || item.Instance.GameBuild is not { } kept)
            return;

        UseCurrentGameBuildPreview preview;
        try
        {
            preview = await Task.Run(() => PreviewUseCurrentGameBuildAsync(services, item.Instance, kept));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowErrorToast(() => Localization.InstanceUseCurrentGameBuild, exception.Message);
            return;
        }

        _useCurrentGameBuildItem = item;
        UseCurrentGameBuildPreview = preview;
    }

    /// <summary>
    /// Checks each mod against the build of the settings with the release from the index, because an amendment can
    /// narrow the bounds of an installed release, and with the installed copy of the release when the index has none.
    /// </summary>
    private static async Task<UseCurrentGameBuildPreview> PreviewUseCurrentGameBuildAsync(BoreaServices services, Instance instance, GameVersion kept)
    {
        var current = services.InstalledVersion.GetInstalledVersion()?.Version;
        var incompatible = new List<string>();
        var unknown = new List<string>();
        foreach (var mod in instance.Mods)
        {
            ModVersionMetadata? release = null;
            try
            {
                release = await services.Mods.GetReleaseAsync(mod.ModId, mod.Version);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
            {
            }

            var metadata = release ?? mod.Metadata;
            var name = metadata.Listing?.Name ?? mod.Metadata.Listing?.Name ?? mod.ModId;
            var compatibility = Compatibility.Evaluate(metadata, current);
            if (compatibility == GameCompatibility.Incompatible)
                incompatible.Add(name);
            else if (compatibility == GameCompatibility.Unknown)
                unknown.Add(name);
        }

        unknown.AddRange(instance.ForeignMods.Select(mod => mod.FolderName));
        return new UseCurrentGameBuildPreview(
            current,
            kept,
            instance.Mods.Count + instance.ForeignMods.Count,
            [.. incompatible.Order(StringComparer.CurrentCultureIgnoreCase)],
            [.. unknown.Order(StringComparer.CurrentCultureIgnoreCase)]);
    }

    [RelayCommand]
    private void CancelUseCurrentGameBuild()
    {
        UseCurrentGameBuildPreview = null;
        _useCurrentGameBuildItem = null;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmUseCurrentGameBuild))]
    private async Task ConfirmUseCurrentGameBuildAsync()
    {
        if (_services is not { } services || _useCurrentGameBuildItem is not { } item || UseCurrentGameBuildPreview is not { Current: { } current } preview)
            return;

        CancelUseCurrentGameBuild();
        var name = item.Name;
        string logText;
        using (var libraryUse = TryUseLibrary())
        {
            if (libraryUse is null)
            {
                ShowErrorToast(() => Localization.InstanceUseCurrentGameBuild, Localization.LibraryFolderBusy);
                return;
            }

            try
            {
                var refusal = await Task.Run(() => services.GameBuilds.UseCurrentBuildAsync(item.InstanceId, CancellationToken.None));
                if (refusal == GameBuildChangeRefusal.None)
                {
                    ShowSuccessToast(() => Localization.FormatToastUsesCurrentGameBuild(name, current.ToString()));
                    logText = $"Instance {item.InstanceId}: left the kept KSA {preview.Kept} for the game folder of the settings.";
                }
                else
                {
                    ShowErrorToast(() => Localization.InstanceUseCurrentGameBuild, refusal == GameBuildChangeRefusal.Busy ? Localization.KeptGameBuildBusy : Localization.GameSaveCloseGame);
                    logText = $"Instance {item.InstanceId}: still on the kept KSA {preview.Kept}, refused as {refusal}.";
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ShowErrorToast(() => Localization.InstanceUseCurrentGameBuild, exception.Message);
                logText = $"Instance {item.InstanceId}: still on the kept KSA {preview.Kept}. {exception.Message}";
            }
        }

        services.Log.Write(logText);
        await ReloadInstancesAsync();
        RefreshKeptGameBuildsIfShown();
    }

    /// <summary>The kept builds, for Settings, Game.</summary>
    public ObservableCollection<KeptGameBuildItem> KeptGameBuilds { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsKeptGameBuilds))]
    private string? _keptGameBuildError;

    /// <summary>The section stays hidden for a player who never kept a build.</summary>
    public bool ShowsKeptGameBuilds => KeptGameBuilds.Count > 0 || KeptGameBuildError is not null;

    /// <summary>Counts the loads, so that a load which a newer one started after drops its result.</summary>
    private int _keptGameBuildLoadCount;

    /// <summary>The loads in flight. They read the Game Builds and Instances folders, so a library move waits for them.</summary>
    private Task _keptGameBuildLoad = Task.CompletedTask;

    internal Task WhenKeptGameBuildsLoadedAsync() => _keptGameBuildLoad;

    private Task StartKeptGameBuildLoad()
    {
        var previous = _keptGameBuildLoad;
        var load = LoadKeptGameBuildsAsync();
        _keptGameBuildLoad = previous.IsCompleted ? load : Task.WhenAll(previous, load);
        return _keptGameBuildLoad;
    }

    /// <summary>
    /// A keep or a clear can end while Settings show the Game tab, for example a long copy. A new game folder
    /// in the settings can make a kept build the game folder of the settings, or stop it being that.
    /// </summary>
    private void RefreshKeptGameBuildsIfShown()
    {
        if (IsSettingsOpen && IsGameTab)
            _ = StartKeptGameBuildLoad();
    }

    private async Task LoadKeptGameBuildsAsync()
    {
        var load = ++_keptGameBuildLoadCount;
        IReadOnlyList<KeptGameBuild> builds = [];
        string? error = null;
        if (_services is { } services)
        {
            try
            {
                builds = await Task.Run(() => services.GameBuilds.ListKeptAsync(CancellationToken.None));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = exception.Message;
            }
        }

        if (load != _keptGameBuildLoadCount)
            return;

        KeptGameBuilds.Clear();
        foreach (var build in builds)
            KeptGameBuilds.Add(new KeptGameBuildItem(this, build));

        KeptGameBuildError = error;
        OnPropertyChanged(nameof(ShowsKeptGameBuilds));
    }

    /// <summary>Deletes the build behind <paramref name="item"/>, after its row asked.</summary>
    internal async Task RemoveKeptGameBuildAsync(KeptGameBuildItem item)
    {
        if (_services is not { } services)
            return;

        var build = item.Version.ToString();
        string? error;
        using (var libraryUse = TryUseLibrary())
        {
            if (libraryUse is null)
            {
                KeptGameBuildError = Localization.LibraryFolderBusy;
                return;
            }

            try
            {
                var refusal = await Task.Run(() => services.GameBuilds.RemoveAsync(item.Version, CancellationToken.None));
                error = refusal switch
                {
                    GameBuildChangeRefusal.GameRunning => Localization.GameSaveCloseGame,
                    GameBuildChangeRefusal.InUse => Localization.SettingsGameBuildInUse,
                    GameBuildChangeRefusal.InstanceUnreadable => Localization.SettingsGameBuildInstanceUnreadable,
                    GameBuildChangeRefusal.GameFolderOfSettings => Localization.SettingsGameBuildIsGameFolder,
                    GameBuildChangeRefusal.Busy => Localization.KeptGameBuildBusy,
                    _ => null,
                };
                services.Log.Write(refusal == GameBuildChangeRefusal.None ? $"Removed the kept KSA {build}." : $"The kept KSA {build} was not removed, refused as {refusal}.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                error = exception.Message;
                services.Log.Write($"The kept KSA {build} was not removed. {exception.Message}");
            }
        }

        await StartKeptGameBuildLoad();
        if (error is not null)
            KeptGameBuildError = error;
    }
}

/// <param name="Current">The build in the game folder of the settings, or null when Borea cannot read it.</param>
/// <param name="Total">The mods of the instance, the ones Borea installed and the ones it found in the folder.</param>
public sealed record UseCurrentGameBuildPreview(GameVersion? Current, GameVersion Kept, int Total, IReadOnlyList<string> Incompatible, IReadOnlyList<string> Unknown);

/// <summary>One kept build in Settings, Game.</summary>
public sealed partial class KeptGameBuildItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly KeptGameBuild _build;

    /// <summary>Remove asks once, the way the other destructive actions do.</summary>
    [ObservableProperty]
    private bool _isConfirmingRemove;

    public KeptGameBuildItem(MainViewModel owner, KeptGameBuild build)
    {
        _owner = owner;
        _build = build;
    }

    public GameVersion Version => _build.Version;

    public string Folder => _build.Folder;

    public string VersionText => _owner.Localization.FormatInstanceGameBuild(_build.Version.ToString());

    public string SizeText => MainViewModel.SizeText(_build.Bytes);

    /// <summary>The instances that keep this build. Null when only the game folder of the settings uses it.</summary>
    public string? UsedByText => _build.InstanceNames.Count > 0
        ? _owner.Localization.FormatSettingsGameBuildUsedBy(string.Join(", ", _build.InstanceNames))
        : _build.IsGameFolderOfSettings ? null : _owner.Localization.SettingsGameBuildUnused;

    public string? GameFolderText => _build.IsGameFolderOfSettings ? _owner.Localization.SettingsGameBuildIsGameFolder : null;

    public bool CanRemove => _build.InstanceNames.Count == 0 && !_build.IsGameFolderOfSettings;

    [RelayCommand]
    private Task RemoveAsync()
    {
        if (!IsConfirmingRemove)
        {
            IsConfirmingRemove = true;
            return Task.CompletedTask;
        }

        return _owner.RemoveKeptGameBuildAsync(this);
    }

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(UsedByText));
        OnPropertyChanged(nameof(GameFolderText));
    }
}
