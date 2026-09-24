using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>
/// The favorite mods and packs. Borea keeps one list for every instance, on
/// this machine only.
/// </summary>
public partial class MainViewModel
{
    private readonly HashSet<string> _favoriteModIds = new(ModIds.Comparer);

    private readonly HashSet<string> _favoritePackIds = new(ModIds.Comparer);

    private Task? _favoritesLoad;

    private readonly SemaphoreSlim _favoriteSaveLock = new(1, 1);

    private Task _favoriteSaves = Task.CompletedTask;

    /// <summary>Completes when every favorite save started so far has finished.</summary>
    internal Task WhenFavoritesSavedAsync() => _favoriteSaves;

    /// <summary>Whether the player marked anything, which is when Discover offers the Only favorites filter.</summary>
    public bool HasFavorites => _favoriteModIds.Count > 0 || _favoritePackIds.Count > 0;

    internal bool IsFavoriteMod(string modId) => _favoriteModIds.Contains(modId);

    internal bool IsFavoritePack(string packId) => _favoritePackIds.Contains(packId);

    internal string FavoriteText(bool isFavorite) => isFavorite ? Localization.ContentRemoveFavorite : Localization.ContentAddFavorite;

    private Task EnsureFavoritesLoadedAsync(BoreaServices services) => _favoritesLoad ??= LoadFavoritesAsync(services);

    private async Task LoadFavoritesAsync(BoreaServices services)
    {
        await ReadFavoritesAsync(_favoriteModIds, services.ModFavorites.GetFavoriteModIdsAsync);
        await ReadFavoritesAsync(_favoritePackIds, services.ModPackFavorites.GetFavoriteModPackIdsAsync);
        OnPropertyChanged(nameof(HasFavorites));
    }

    /// <summary>A file that cannot be read leaves its rows unmarked, and Discover loads as usual.</summary>
    private async Task ReadFavoritesAsync(HashSet<string> ids, Func<CancellationToken, Task<IReadOnlyList<string>>> read)
    {
        try
        {
            ids.UnionWith(await read(CancellationToken.None));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowErrorToast(() => Localization.ToastFavoritesReadFailed, exception.Message);
        }
    }

    internal Task ToggleFavoriteAsync(DiscoverItem item)
    {
        if (_services is not { } services)
            return Task.CompletedTask;

        void Mark(bool isFavorite)
        {
            MarkFavorite(_favoriteModIds, item.ModId, isFavorite);
            foreach (var row in ModRows(item))
                row.IsFavorite = isFavorite;
        }

        return SaveFavoriteAsync(item.Name, !item.IsFavorite, Mark, favorite => favorite
            ? services.ModFavorites.AddFavoriteAsync(item.ModId)
            : services.ModFavorites.RemoveFavoriteAsync(item.ModId));
    }

    internal Task ToggleFavoriteAsync(PackItem pack)
    {
        if (_services is not { } services)
            return Task.CompletedTask;

        void Mark(bool isFavorite)
        {
            MarkFavorite(_favoritePackIds, pack.PackId, isFavorite);
            foreach (var row in _packs.Append(SelectedPack).Append(pack).OfType<PackItem>().Where(row => ModIds.Equals(row.PackId, pack.PackId)))
                row.IsFavorite = isFavorite;
        }

        return SaveFavoriteAsync(pack.Name, !pack.IsFavorite, Mark, favorite => favorite
            ? services.ModPackFavorites.AddFavoriteAsync(pack.PackId)
            : services.ModPackFavorites.RemoveFavoriteAsync(pack.PackId));
    }

    /// <summary>
    /// Every row that can show the mod. An index reload builds new Discover rows,
    /// while the open page and the instance rows keep the ones from before.
    /// </summary>
    private IEnumerable<DiscoverItem> ModRows(DiscoverItem item)
        => _listings.Concat(_content.Select(content => content.Page)).Append(SelectedContent).Append(item)
            .OfType<DiscoverItem>().Where(row => ModIds.Equals(row.ModId, item.ModId));

    private static void MarkFavorite(HashSet<string> ids, string id, bool isFavorite)
    {
        if (isFavorite)
            ids.Add(id);
        else
            ids.Remove(id);
    }

    /// <summary>
    /// Marks the row at once and saves behind <see cref="_favoriteSaveLock"/>,
    /// because every save rewrites the whole file. A failed save takes the mark back.
    /// </summary>
    private Task SaveFavoriteAsync(string name, bool favorite, Action<bool> mark, Func<bool, Task> save)
    {
        ShowFavorite(mark, favorite);
        var saving = RunFavoriteSaveAsync(name, favorite, mark, save);
        _favoriteSaves = Task.WhenAll(_favoriteSaves, saving);
        return saving;
    }

    private async Task RunFavoriteSaveAsync(string name, bool favorite, Action<bool> mark, Func<bool, Task> save)
    {
        await _favoriteSaveLock.WaitAsync();
        try
        {
            await save(favorite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowFavorite(mark, !favorite);
            ShowErrorToast(() => Localization.FormatToastFavoriteFailed(name), exception.Message);
        }
        finally
        {
            _favoriteSaveLock.Release();
        }
    }

    private void ShowFavorite(Action<bool> mark, bool favorite)
    {
        mark(favorite);
        OnPropertyChanged(nameof(HasFavorites));
        // the filter goes away with the last favorite, so it cannot stay on with nothing to show
        if (!HasFavorites)
            FavoritesOnly = false;
        else if (FavoritesOnly)
            ApplyDiscoverFilters();
    }
}
