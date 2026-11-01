using System.Collections.Generic;
using System.Linq;
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
        if (rows.FirstOrDefault(row => row.CanRemove) is { } first)
            first.IsPrimary = true;
    }

    public LocalizationService Localization => _owner.Localization;

    public IReadOnlyList<ConflictRow> Rows { get; }

    /// <summary>A removal runs the plan again, and another conflict that is still there comes back to this dialog.</summary>
    public bool ShowsAgainNote => Rows.Any(row => row.CanRemove);

    [RelayCommand]
    private void Close() => _owner.ConflictDialog = null;
}
