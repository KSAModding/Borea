using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// One pair of mods in the conflict modal. The row offers to remove the other mod when <see cref="RemoveText"/> is set,
/// and otherwise links to its page when <see cref="OpenPageText"/> is set.
/// </summary>
public sealed partial class ConflictRow
{
    private readonly Func<Task> _remove;
    private readonly Func<Task> _openPage;

    internal ConflictRow(string text, string? reasonText, string? removeText, string? openPageText, Func<Task> remove, Func<Task> openPage)
    {
        Text = text;
        ReasonText = reasonText;
        RemoveText = removeText;
        OpenPageText = openPageText;
        _remove = remove;
        _openPage = openPage;
    }

    /// <summary>Names both mods and the one that declares the conflict.</summary>
    public string Text { get; }

    /// <summary>Why Borea does not remove the other mod, or null.</summary>
    public string? ReasonText { get; }

    public string? RemoveText { get; }

    public string? OpenPageText { get; }

    public bool CanRemove => RemoveText is not null;

    public bool CanOpenPage => OpenPageText is not null;

    [RelayCommand]
    private Task RemoveAsync() => _remove();

    [RelayCommand]
    private Task OpenPageAsync() => _openPage();
}
