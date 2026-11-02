using System;
using System.Collections.ObjectModel;
using Borea.Core.Mods;
using Borea.Core.Preferences;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>One group of tiles on Home, and its link to the Discover tab and sort that show the same order.</summary>
public sealed partial class HomeGroup : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly Func<string> _title;
    private readonly Func<string> _linkText;
    private readonly ContentType _type;
    private readonly DiscoverSortOrder _sort;

    public ObservableCollection<RecentItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public string Title => _title();

    public string LinkText => _linkText();

    public HomeGroup(MainViewModel owner, Func<string> title, Func<string> linkText, ContentType type, DiscoverSortOrder sort)
    {
        _owner = owner;
        _title = title;
        _linkText = linkText;
        _type = type;
        _sort = sort;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasItems));
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LinkText));
        foreach (var item in Items)
            item.RefreshText();
    }

    [RelayCommand]
    private void OpenDiscover() => _owner.OpenDiscoverTab(_type, _sort);
}
