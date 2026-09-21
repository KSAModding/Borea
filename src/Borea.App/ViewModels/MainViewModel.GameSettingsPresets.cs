using Borea.App.Localization;
using Borea.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Borea.App.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<GameSettingsPresetItem> GameSettingsPresets { get; } = [];

    [ObservableProperty]
    private GameSettingsPresetItem? _selectedGameSettingsPreset;

    [ObservableProperty]
    private bool _isCreatingGameSettingsPreset;

    [ObservableProperty]
    private string _newGameSettingsPresetName = string.Empty;

    [ObservableProperty]
    private string? _gameSettingsPresetError;

    private Guid _gameSettingsPresetSourceInstanceId;

    private async Task LoadGameSettingsPresetsAsync()
    {
        GameSettingsPresets.Clear();
        var none = GameSettingsPresetItem.CreateNone(this);
        GameSettingsPresets.Add(none);
        SelectedGameSettingsPreset = none;
        if (_services is null)
            return;

        try
        {
            var presets = await _services.GameSettingsPresets.ListAsync();
            foreach (var preset in presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
                GameSettingsPresets.Add(new GameSettingsPresetItem(preset));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // the picker just stays at "No Preset"; creation still works
        }
    }

    internal void BeginCreateGameSettingsPreset(Guid instanceId)
    {
        if (_services?.InstalledVersion.GetInstalledVersion() is null)
        {
            ShowErrorToast(() => Localization.GameDataCreatePreset, Localization.ModalSettingsPresetNoGameVersion);
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

        // Re-checked here: the installed version could disappear between opening the modal and confirming.
        if (services.InstalledVersion.GetInstalledVersion()?.Version is not { } version)
        {
            GameSettingsPresetError = Localization.ModalSettingsPresetNoGameVersion;
            return;
        }

        var sourcePath = services.Paths.GetInstanceSettingsPath(_gameSettingsPresetSourceInstanceId);
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
    }
}

public sealed class GameSettingsPresetItem
{
    public Guid? Id { get; }
    public string Name { get; }
    public string? VersionText { get; }

    /// <summary>The "no preset" row.</summary>
    private GameSettingsPresetItem(MainViewModel owner)
    {
        Id = null;
        Name = owner.Localization.NoPreset;
    }

    public GameSettingsPresetItem(GameSettingsPreset preset)
    {
        Id = preset.Id;
        Name = preset.Name;
        VersionText = preset.Version.ToString();
    }

    internal static GameSettingsPresetItem CreateNone(MainViewModel owner) => new(owner);
}
