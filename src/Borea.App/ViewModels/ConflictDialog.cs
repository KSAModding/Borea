using System.Collections.Generic;
using Borea.App.Localization;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The modal that says why a plan stopped on mods that do not work together, with one row per pair of mods.</summary>
public sealed partial class ConflictDialog
{
    private readonly MainViewModel _owner;

    public ConflictDialog(MainViewModel owner, IReadOnlyList<ConflictRow> rows)
    {
        _owner = owner;
        Rows = rows;
    }

    public LocalizationService Localization => _owner.Localization;

    public IReadOnlyList<ConflictRow> Rows { get; }

    [RelayCommand]
    private void Close() => _owner.ConflictDialog = null;
}
