using Borea.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
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
        SelectedGameSettingsPreset = null;
        if (_services is null)
            return;

        try
        {
            foreach (var preset in await _services.GameSettingsPresets.ListAsync())
                GameSettingsPresets.Add(new GameSettingsPresetItem(preset));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // the picker just stays empty; creation still works without a preset
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

public sealed class GameSettingsPresetItem(GameSettingsPreset preset)
{
    public Guid Id { get; } = preset.Id;
    public string Name { get; } = preset.Name;
    public string VersionText { get; } = preset.Version.ToString();
}
