using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Borea.Core.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Game data tab of the instance page.
/// </summary>
public partial class MainViewModel
{
    private static readonly string[] SizeUnits = ["KB", "MB", "GB", "TB"];

    private IReadOnlyList<GameDataEntry> _gameData = [];

    public ObservableCollection<GameDataItem> GameDataItems { get; } = [];

    [ObservableProperty]
    private string? _gameDataError;

    [RelayCommand]
    private async Task ShowInstanceGameDataAsync()
    {
        InstanceTab = InstanceTab.GameData;
        await LoadGameDataAsync();
    }

    private async Task LoadGameDataAsync()
    {
        GameDataError = null;
        _gameData = [];
        RefreshGameDataItems();
        if (_services is null || SelectedInstance is null)
            return;

        var instanceId = SelectedInstance.InstanceId;
        try
        {
            var gameData = await _services.GameData.ReadAsync(instanceId);
            if (SelectedInstance?.InstanceId != instanceId)
                return;

            _gameData = gameData;
            RefreshGameDataItems();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (SelectedInstance?.InstanceId == instanceId)
                GameDataError = exception.Message;
        }
    }

    private void RefreshGameDataItems()
    {
        GameDataItems.Clear();
        foreach (var entry in _gameData)
            GameDataItems.Add(new GameDataItem(this, entry));
    }

    internal void OpenGameDataFolder(string folder) => GameDataError = TryOpenWithSystem(folder);

    internal string FormatGameDataSize(long bytes)
    {
        if (bytes <= 0)
            return Localization.GameDataEmpty;

        if (bytes < 1000)
            return bytes.ToString(CultureInfo.CurrentCulture) + " B";

        var value = bytes / 1000.0;
        var unit = 0;
        while (value >= 999.95 && unit < SizeUnits.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return value.ToString("0.0", CultureInfo.CurrentCulture) + " " + SizeUnits[unit];
    }
}

/// <summary>
/// One folder or file on the Game data tab. A file opens the folder it is in.
/// </summary>
public sealed partial class GameDataItem
{
    private readonly MainViewModel _owner;

    public string Name { get; }

    public string SizeText { get; }

    public bool Exists { get; }

    public string FolderPath { get; }

    public GameDataItem(MainViewModel owner, GameDataEntry entry)
    {
        _owner = owner;
        Name = entry.Name;
        SizeText = owner.FormatGameDataSize(entry.SizeBytes);
        Exists = entry.Exists;
        FolderPath = entry.IsFolder ? entry.Path : Path.GetDirectoryName(entry.Path) ?? entry.Path;
    }

    [RelayCommand]
    private void OpenFolder() => _owner.OpenGameDataFolder(FolderPath);
}
