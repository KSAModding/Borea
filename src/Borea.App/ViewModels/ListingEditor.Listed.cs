using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>A listed mod that the start step can load to change it.</summary>
public sealed record ListedListing(string Id, string Name, bool IsOwn);

/// <summary>
/// The search for a listed mod on the start step. The listings of the signed-in GitHub account come first.
/// </summary>
public sealed partial class ListingEditor
{
    private IReadOnlyList<ContentIndexListing> _listedMods = [];

    [ObservableProperty]
    private string _listedQuery = string.Empty;

    /// <summary>The listed mods whose id or name contains the query, in any letter case.</summary>
    public ObservableCollection<ListedListing> ListedMatches { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadListedCommand))]
    private ListedListing? _selectedListed;

    public bool HasNoListedMatch => ListedMatches.Count == 0 && _listedMods.Count > 0;

    /// <summary>Moves the selection through the matches, as the arrow keys in the search field do.</summary>
    internal void MoveListedSelection(int step)
    {
        if (ListedMatches.Count == 0)
            return;

        var index = SelectedListed is { } selected ? ListedMatches.IndexOf(selected) + step : step > 0 ? 0 : ListedMatches.Count - 1;
        SelectedListed = ListedMatches[Math.Clamp(index, 0, ListedMatches.Count - 1)];
    }

    private void SetListedMods(ContentIndexSnapshot? snapshot)
    {
        _listedMods = snapshot?.Listings
            .Where(listing => listing.Authored?.Type is ContentType.Mod or ContentType.ModLoader)
            .ToList() ?? [];
        FindListed();
    }

    private void FindListed()
    {
        var login = IsSignedIn ? _owner.GitHubLogin : null;
        var query = ListedQuery.Trim();
        var matches = _listedMods
            .Select(listing => new ListedListing(listing.Id, listing.Authored!.Name, login is not null && IsOwnedBy(listing.Authored, login)))
            .Where(listing => listing.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || listing.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(listing => listing.IsOwn)
            .ThenBy(listing => listing.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        MainViewModel.Arrange(ListedMatches, matches);
        SelectedListed = matches.FirstOrDefault(listing => listing.Id == SelectedListed?.Id) ?? (query.Length > 0 ? matches.FirstOrDefault() : null);
        OnPropertyChanged(nameof(HasNoListedMatch));
    }

    /// <summary>
    /// A listing is your own when the owner of a GitHub repository in its [releases] is the signed-in login.
    /// GitHub logins ignore letter case. A repository of an organization counts only when the login is that name.
    /// </summary>
    private static bool IsOwnedBy(ModMetadata listing, string login) =>
        listing.Releases?.Hosts.Any(host =>
            string.Equals(host.Host, ListingAuthority.GitHub, StringComparison.OrdinalIgnoreCase)
            && string.Equals(host.Reference.Split('/')[0], login, StringComparison.OrdinalIgnoreCase)) == true;

    partial void OnListedQueryChanged(string value) => FindListed();
}
