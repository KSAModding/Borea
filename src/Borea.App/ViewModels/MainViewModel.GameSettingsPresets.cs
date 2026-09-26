using Borea.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Borea.App.ViewModels;

/// <summary>
/// Game settings presets: saving one from an instance, picking one while a new
/// instance is created, and the saved presets in the Game settings.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The picker of the new instance modal, with "No preset" first.</summary>
    public ObservableCollection<GameSettingsPresetItem> GameSettingsPresets { get; } = [];

    /// <summary>The saved presets, for the Game settings, without the "No preset" row.</summary>
    public ObservableCollection<GameSettingsPresetItem> SavedGameSettingsPresets { get; } = [];

    [ObservableProperty]
    private GameSettingsPresetItem? _selectedGameSettingsPreset;

    /// <summary>
    /// The picker belongs to a plain new instance. A pack and a shared profile
    /// bring the settings of what they install, so they do not offer it.
    /// </summary>
    public bool CanPickGameSettingsPreset => IsCreatingInstance && !IsImportingSharedProfile && _newInstancePack is null;

    [ObservableProperty]
    private bool _isCreatingGameSettingsPreset;

    [ObservableProperty]
    private string _newGameSettingsPresetName = string.Empty;

    [ObservableProperty]
    private string? _gameSettingsPresetError;

    private Guid _gameSettingsPresetSourceInstanceId;

    private Task _gameSettingsPresetLoad = Task.CompletedTask;

    internal Task WhenGameSettingsPresetsLoadedAsync() => _gameSettingsPresetLoad;

    /// <summary>Starts a read without awaiting it, for a caller that only opens a modal.</summary>
    private void StartGameSettingsPresetLoad()
    {
        var previous = _gameSettingsPresetLoad;
        var load = LoadGameSettingsPresetsAsync();
        _gameSettingsPresetLoad = previous.IsCompleted ? load : Task.WhenAll(previous, load);
    }

    /// <summary>Reads the saved presets into both lists. A folder Borea cannot read is left out.</summary>
    internal async Task LoadGameSettingsPresetsAsync()
    {
        GameSettingsPresets.Clear();
        SavedGameSettingsPresets.Clear();
        var none = GameSettingsPresetItem.None(this);
        GameSettingsPresets.Add(none);
        SelectedGameSettingsPreset = none;
        if (_services is null)
            return;

        try
        {
            var presets = await _services.GameSettingsPresets.ListAsync();
            foreach (var preset in presets.OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                GameSettingsPresets.Add(new GameSettingsPresetItem(this, preset));
                SavedGameSettingsPresets.Add(new GameSettingsPresetItem(this, preset));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            GameSettingsPresetError = exception.Message;
        }
    }

    /// <summary>Opens the modal that saves the settings of <paramref name="instanceId"/> as a preset.</summary>
    internal void BeginCreateGameSettingsPreset(Guid instanceId)
    {
        if (_services?.InstalledVersion.GetInstalledVersion() is null)
        {
            ShowErrorToast(() => Localization.PresetModalTitle, Localization.ModalSettingsPresetNoGameVersion);
            return;
        }

        _gameSettingsPresetSourceInstanceId = instanceId;
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

        if (_services is not { } services)
            return;

        // read again: the installed version can go between opening the modal and confirming
        if (services.InstalledVersion.GetInstalledVersion()?.Version is not { } version)
        {
            GameSettingsPresetError = Localization.ModalSettingsPresetNoGameVersion;
            return;
        }

        var sourcePath = services.Paths.GetInstanceSettingsPath(_gameSettingsPresetSourceInstanceId);
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

/// <summary>One saved preset, in the picker of the new instance modal and in the Game settings.</summary>
public sealed partial class GameSettingsPresetItem : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>Null on the "No preset" row, which leaves the instance with the settings the game writes.</summary>
    public Guid? Id { get; }

    public string Name { get; }

    /// <summary>The game version the preset was saved from. Null on the "No preset" row.</summary>
    public string? VersionText { get; }

    /// <summary>Delete asks once, the way the other destructive actions do.</summary>
    [ObservableProperty]
    private bool _isConfirmingDelete;

    public GameSettingsPresetItem(MainViewModel owner, GameSettingsPreset preset)
    {
        _owner = owner;
        Id = preset.Id;
        Name = preset.Name;
        VersionText = preset.Version.ToString();
    }

    private GameSettingsPresetItem(MainViewModel owner)
    {
        _owner = owner;
        Name = owner.Localization.NoPreset;
    }

    internal static GameSettingsPresetItem None(MainViewModel owner) => new(owner);

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
