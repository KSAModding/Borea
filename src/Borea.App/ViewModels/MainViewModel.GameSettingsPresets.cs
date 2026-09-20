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
}

public sealed class GameSettingsPresetItem(GameSettingsPreset preset)
{
    public Guid Id { get; } = preset.Id;
    public string Name { get; } = preset.Name;
    public string VersionText { get; } = preset.Version.ToString();
}
