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

    internal string FormatGameDataSize(long bytes) => bytes <= 0 ? Localization.GameDataEmpty : SizeText(bytes);

    /// <summary>A byte count in decimal units, the way the download progress counts them: "38.0 MB".</summary>
    internal static string SizeText(long bytes)
    {
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

    /// <summary>
    /// A count the way a row shows it: 999, 1.2k, 15k, 1.2M. The exact number
    /// goes into the tooltip. Values are rounded down, so 1,999 reads 1.9k
    /// and never rounds up to a figure the mod has not reached.
    /// </summary>
    internal static string CompactCount(long count)
    {
        if (count < 1000)
            return count.ToString(CultureInfo.CurrentCulture);

        return count < 1_000_000
            ? Compact(count / 1000.0, "k")
            : Compact(count / 1_000_000.0, "M");
    }

    private static string Compact(double value, string unit)
    {
        var rounded = value < 10 ? Math.Floor(value * 10) / 10 : Math.Floor(value);
        return rounded.ToString(value < 10 ? "0.#" : "0", CultureInfo.CurrentCulture) + unit;
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
