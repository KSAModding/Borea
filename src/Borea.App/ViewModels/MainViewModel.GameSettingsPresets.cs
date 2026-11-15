using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Borea.App.ViewModels;

/// <summary>
/// Game settings presets: saving one from an instance, picking the settings a new
/// instance starts with, and the saved presets in the Game settings. It also copies the settings of an instance
/// to the game without mods.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// The picker of the new instance modal: the settings of the game while the game has a settings.toml,
    /// then the game defaults, then the saved presets.
    /// </summary>
    public ObservableCollection<GameSettingsPresetItem> GameSettingsPresets { get; } = [];

    /// <summary>The saved presets, for the Game settings, without the two rows that are not presets.</summary>
    public ObservableCollection<GameSettingsPresetItem> SavedGameSettingsPresets { get; } = [];

    [ObservableProperty]
    private GameSettingsPresetItem? _selectedGameSettingsPreset;

    /// <summary>The game without mods has no settings.toml, so the picker leaves out the row of the game's settings and says why.</summary>
    [ObservableProperty]
    private bool _isGameSettingsFileMissing;

    /// <summary>
    /// The picker belongs to a plain new instance. An import from the game and a pack instance offer no choice and
    /// always start with a copy of the game's settings.toml, which the instance repository makes when it creates them.
    /// </summary>
    public bool CanPickGameSettingsPreset => IsCreatingInstance && !IsImportingSharedProfile && _newInstancePack is null;

    [ObservableProperty]
    private bool _isCreatingGameSettingsPreset;

    [ObservableProperty]
    private string _newGameSettingsPresetName = string.Empty;

    [ObservableProperty]
    private string? _gameSettingsPresetError;

    private Instance? _gameSettingsPresetSource;

    private Task _gameSettingsPresetLoad = Task.CompletedTask;

    internal Task WhenGameSettingsPresetsLoadedAsync() => _gameSettingsPresetLoad;

    /// <summary>Where a load of the presets reads them.</summary>
    internal Func<BoreaServices, IGameSettingsPresetRepository> GameSettingsPresetLoadSource { get; set; } = services => services.GameSettingsPresets;

    /// <summary>Counts the loads, so that a load which a newer one started after drops its result.</summary>
    private int _gameSettingsPresetLoadCount;

    /// <summary>
    /// Starts a read without awaiting it, for a caller that only opens a modal.
    /// The picker starts at its first row again, which is the settings of the game when the game has a settings.toml.
    /// </summary>
    private void StartGameSettingsPresetLoad()
    {
        SetFixedGameSettingsRows();
        SelectedGameSettingsPreset = GameSettingsPresets[0];
        _gameSettingsPresetLoad = Chain(_gameSettingsPresetLoad, LoadGameSettingsPresetsAsync());
    }

    /// <summary>
    /// Puts the rows that are not presets at the top of the picker. The row of the game's settings is there only
    /// while the game has a settings.toml.
    /// </summary>
    private void SetFixedGameSettingsRows()
    {
        IsGameSettingsFileMissing = _services?.SharedProfileSettings.Exists() != true;
        for (var index = GameSettingsPresets.Count - 1; index >= 0; index--)
        {
            if (GameSettingsPresets[index].Id is null)
                GameSettingsPresets.RemoveAt(index);
        }

        GameSettingsPresets.Insert(0, GameSettingsPresetItem.GameDefaults(this));
        if (!IsGameSettingsFileMissing)
            GameSettingsPresets.Insert(0, GameSettingsPresetItem.FromGame(this));
    }

    /// <summary>
    /// Reads the saved presets and then replaces both lists. A folder Borea cannot
    /// read is left out. The selected row stays selected while it is still offered.
    /// </summary>
    internal async Task LoadGameSettingsPresetsAsync()
    {
        var load = ++_gameSettingsPresetLoadCount;
        IReadOnlyList<GameSettingsPreset> presets = [];
        string? error = null;
        if (_services is not null)
        {
            try
            {
                presets = await GameSettingsPresetLoadSource(_services).ListAsync();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                error = exception.Message;
            }
        }

        if (load != _gameSettingsPresetLoadCount)
            return;

        if (error is not null)
            GameSettingsPresetError = error;

        // the picker can clear its selection when its items go, so read it first
        var selected = SelectedGameSettingsPreset?.Source;
        GameSettingsPresets.Clear();
        SavedGameSettingsPresets.Clear();
        SetFixedGameSettingsRows();
        foreach (var preset in presets.OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            GameSettingsPresets.Add(new GameSettingsPresetItem(this, preset));
            SavedGameSettingsPresets.Add(new GameSettingsPresetItem(this, preset));
        }

        SelectedGameSettingsPreset = GameSettingsPresets.FirstOrDefault(item => Equals(item.Source, selected)) ?? GameSettingsPresets[0];
    }

    /// <summary>Tells the player that a new instance came without a copy of the game's settings, when <paramref name="settingsError"/> says why.</summary>
    private void ShowSettingsNotCopied(string instanceName, string? settingsError)
    {
        if (settingsError is not null)
            ShowErrorToast(() => Localization.FormatToastSettingsNotCopied(instanceName), settingsError);
    }

    /// <summary>Opens the modal that saves the settings of <paramref name="instance"/> as a preset.</summary>
    internal void BeginCreateGameSettingsPreset(Instance instance)
    {
        if (_services?.InstanceGames.Resolve(instance).Version is null)
        {
            ShowErrorToast(() => Localization.PresetModalTitle, Localization.ModalSettingsPresetNoGameVersion);
            return;
        }

        _gameSettingsPresetSource = instance;
        GameSettingsPresetError = null;
        NewGameSettingsPresetName = string.Empty;
        IsCreatingGameSettingsPreset = true;
    }

    [RelayCommand]
    private void CancelGameSettingsPresetModal()
    {
        GameSettingsPresetError = null;
        IsCreatingGameSettingsPreset = false;
    }

    [RelayCommand]
    private async Task ConfirmGameSettingsPresetModalAsync()
    {
        var name = NewGameSettingsPresetName.Trim();
        if (name.Length == 0)
        {
            GameSettingsPresetError = Localization.ModalNameRequired;
            return;
        }

        if (_services is not { } services || _gameSettingsPresetSource is not { } source)
            return;

        // read again: the installed version can go between opening the modal and confirming
        if (GameVersionOf(services, source) is not { } version)
        {
            GameSettingsPresetError = Localization.ModalSettingsPresetNoGameVersion;
            return;
        }

        var sourcePath = services.Paths.GetInstanceSettingsPath(source.InstanceId);
        if (!File.Exists(sourcePath))
        {
            GameSettingsPresetError = Localization.PresetModalNoSettings;
            return;
        }

        try
        {
            await services.GameSettingsPresets.SaveAsync(name, version, sourcePath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            GameSettingsPresetError = exception.Message;
            return;
        }

        IsCreatingGameSettingsPreset = false;
        NewGameSettingsPresetName = string.Empty;
        ShowSuccessToast(() => Localization.FormatToastPresetSaved(name));
        await LoadGameSettingsPresetsAsync();
    }

    /// <summary>The instance whose settings the modal offers to copy to the game without mods. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCopySettingsToGameModalOpen), nameof(CopySettingsToGameText))]
    private InstanceItem? _copyingSettingsToGame;

    [ObservableProperty]
    private string? _copySettingsToGameError;

    public bool IsCopySettingsToGameModalOpen => CopyingSettingsToGame is not null;

    public string? CopySettingsToGameText => CopyingSettingsToGame is { } item && _services is { } services
        ? Localization.FormatCopySettingsToGameText(item.Name, WithoutUserProfile(services.SharedProfileSettings.BackupFolder))
        : null;

    /// <summary>
    /// Opens the modal that copies the settings of <paramref name="item"/> to the game without mods. While the game runs,
    /// or before the instance has a settings.toml, a toast says why instead.
    /// </summary>
    internal void BeginCopySettingsToGame(InstanceItem item)
    {
        if (_services is not { } services)
            return;

        if (IsGameRunning(services))
        {
            ShowErrorToast(() => Localization.FormatToastSettingsToGameFailed(item.Name), Localization.CopySettingsToGameGameRunning);
            return;
        }

        if (!File.Exists(services.Paths.GetInstanceSettingsPath(item.InstanceId)))
        {
            ShowErrorToast(() => Localization.FormatToastSettingsToGameFailed(item.Name), Localization.CopySettingsToGameNoSettings);
            return;
        }

        CopySettingsToGameError = null;
        CopyingSettingsToGame = item;
    }

    [RelayCommand]
    private void CancelCopySettingsToGame()
    {
        CopySettingsToGameError = null;
        CopyingSettingsToGame = null;
    }

    /// <summary>
    /// Replaces the game's settings.toml with the settings of the instance in the modal. The game can start between
    /// the open and the confirm, so the check runs again here and in the storage.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmCopySettingsToGameAsync()
    {
        if (_services is not { } services || CopyingSettingsToGame is not { } item)
            return;

        if (IsGameRunning(services))
        {
            CopySettingsToGameError = Localization.CopySettingsToGameGameRunning;
            return;
        }

        SharedProfileSettingsCopy copy;
        try
        {
            copy = await services.SharedProfileSettings.CopyFromInstanceAsync(item.InstanceId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CopySettingsToGameError = exception.Message;
            return;
        }

        switch (copy.Outcome)
        {
            case SharedProfileSettingsCopyOutcome.GameRunning:
                CopySettingsToGameError = Localization.CopySettingsToGameGameRunning;
                return;
            case SharedProfileSettingsCopyOutcome.NoInstanceSettings:
                CopySettingsToGameError = Localization.CopySettingsToGameNoSettings;
                return;
        }

        CopySettingsToGameError = null;
        CopyingSettingsToGame = null;
        services.Log.Write($"Copied the settings.toml of instance {item.InstanceId} to the game without mods. Old file: {copy.BackupPath ?? "none"}.");
        var name = item.Name;
        if (copy.BackupPath is not { } backup)
        {
            ShowSuccessToast(() => Localization.FormatToastSettingsCopiedToGame(name));
            return;
        }

        // the toast names the backup file and opens its folder, because a full path does not fit the toast
        var folder = Path.GetDirectoryName(backup) ?? services.SharedProfileSettings.BackupFolder;
        Toasts.ShowMessage(
            ToastKind.Success,
            () => Localization.FormatToastSettingsCopiedToGame(name),
            Localization.FormatToastSettingsBackup(Path.GetFileName(backup)),
            new ToastAction(() => Localization.LibraryOpenFolder, () => ShowOpenError(() => PathName(folder), TryOpenWithSystem(folder))));
    }

    /// <summary>Removes the preset behind <paramref name="item"/>, after its row asked.</summary>
    internal async Task DeleteGameSettingsPresetAsync(GameSettingsPresetItem item)
    {
        if (_services is not { } services || item.Id is not { } id)
            return;

        GameSettingsPresetError = null;
        try
        {
            await services.GameSettingsPresets.DeleteAsync(id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GameSettingsPresetError = exception.Message;
            return;
        }

        await LoadGameSettingsPresetsAsync();
    }
}

/// <summary>
/// One row of the picker of the new instance modal, or one saved preset in the Game settings.
/// The picker also has a row for the settings of the game and one for the game defaults.
/// </summary>
public sealed partial class GameSettingsPresetItem : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>The id of the preset. Null on the rows of the game's settings and the game defaults.</summary>
    public Guid? Id { get; }

    public string Name { get; }

    /// <summary>The game version the preset was saved from. Null on the rows that are not presets.</summary>
    public string? VersionText { get; }

    /// <summary>What a new instance starts with when this row is picked.</summary>
    public InstanceSettingsSource Source { get; }

    /// <summary>Delete asks once, the way the other destructive actions do.</summary>
    [ObservableProperty]
    private bool _isConfirmingDelete;

    public GameSettingsPresetItem(MainViewModel owner, GameSettingsPreset preset)
    {
        _owner = owner;
        Id = preset.Id;
        Name = preset.Name;
        VersionText = preset.Version.ToString();
        Source = new InstanceSettingsSource.FromPreset(preset.Id);
    }

    private GameSettingsPresetItem(MainViewModel owner, string name, InstanceSettingsSource source)
    {
        _owner = owner;
        Name = name;
        Source = source;
    }

    internal static GameSettingsPresetItem FromGame(MainViewModel owner) =>
        new(owner, owner.Localization.GameSettingsFromGame, InstanceSettingsSource.FromGame.Value);

    internal static GameSettingsPresetItem GameDefaults(MainViewModel owner) =>
        new(owner, owner.Localization.GameSettingsGameDefaults, InstanceSettingsSource.GameDefaults.Value);

    [RelayCommand]
    private Task DeleteAsync()
    {
        if (!IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            return Task.CompletedTask;
        }

        return _owner.DeleteGameSettingsPresetAsync(this);
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;
}
