using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Mods;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Statistics tab of the Steward page: four cards with what <see cref="IndexStatistics"/> counts, the most useful card first.
/// It reads the snapshot that Borea already holds, so a read of the tab makes no request.
/// </summary>
public sealed partial class StewardStatisticsTab : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;
    private IndexStatistics? _shown;

    public StewardStatisticsTab(MainViewModel owner)
    {
        _owner = owner;
    }

    [ObservableProperty]
    private IReadOnlyList<StewardStatisticsCard> _cards = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isLoaded;

    internal Task WhenLoadedAsync() => _load;

    /// <summary>Counts again from the snapshot that Borea holds now. A second call during a read joins it.</summary>
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    /// <summary>Builds the texts of the tab again in the language that is now selected.</summary>
    internal void RefreshText() => Show(_shown);

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            Show(IndexStatistics.From(await services.CachedIndexSnapshots.GetSnapshotAsync(), DateTimeOffset.UtcNow));
            IsLoaded = true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Show(null);
            Error = _owner.Localization.FormatStewardWatcherGoneFailed(exception.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Show(IndexStatistics? statistics)
    {
        _shown = statistics;
        Cards = statistics is null ? [] : [GameSupportCard(statistics), DependenciesCard(statistics), UpkeepCard(statistics), OverviewCard(statistics)];
    }

    private StewardStatisticsCard GameSupportCard(IndexStatistics statistics)
    {
        var text = _owner.Localization;
        var unsupported = statistics.ModsNotOnCurrentBuild;
        var summary = statistics.CurrentBuild is { } build
            ? text.FormatStewardStatisticsSupport(Number(statistics.ModsOnCurrentBuild), Number(statistics.ModsOnCurrentBuild + unsupported.Count), build.ToString())
            : text.StewardStatisticsNoBuild;
        return new StewardStatisticsCard(text.StewardStatisticsGameSupport, summary, statistics.CurrentBuild is null ? [] : [Listings(text.FormatStewardStatisticsUnsupported(Number(unsupported.Count)), unsupported)]);
    }

    private StewardStatisticsCard DependenciesCard(IndexStatistics statistics)
    {
        var text = _owner.Localization;
        return new StewardStatisticsCard(text.StewardStatisticsDependencies, null,
        [
            new StewardStatisticsSection(text.StewardStatisticsMostDependencies, [.. statistics.MostDependencies.Select(row =>
                Row(row.Name, row.Id, text.FormatStewardStatisticsDependencyCount(Number(row.Required), Number(row.Optional))))]),
            new StewardStatisticsSection(text.StewardStatisticsMostDependedOn, [.. statistics.MostDependedOn.Select(row =>
                Row(row.Name, row.IsListed ? row.Id : null, text.FormatStewardStatisticsDependentCount(Number(row.Required), Number(row.Optional))))]),
        ]);
    }

    private StewardStatisticsCard UpkeepCard(IndexStatistics statistics)
    {
        var text = _owner.Localization;
        var always = new[]
        {
            Listings(text.FormatStewardStatisticsNoUpdate(Number(IndexStatistics.NoUpdateDays), Number(statistics.NoRecentUpdate.Count)), statistics.NoRecentUpdate),
            Listings(text.FormatStewardStatisticsNoImage(Number(statistics.NoImage.Count)), statistics.NoImage),
            Listings(text.FormatStewardStatisticsNoForumsThread(Number(statistics.NoForumsThread.Count)), statistics.NoForumsThread),
        };

        // without a build nothing was rated, so a count here would claim a zero that nobody checked
        return new StewardStatisticsCard(text.StewardStatisticsUpkeep, null, statistics.CurrentBuild is null
            ? always
            : [Listings(text.FormatStewardStatisticsNoCurrentRelease(Number(statistics.NoCurrentRelease.Count)), statistics.NoCurrentRelease), .. always]);
    }

    private StewardStatisticsCard OverviewCard(IndexStatistics statistics)
    {
        var text = _owner.Localization;
        return new StewardStatisticsCard(text.StewardStatisticsOverview, null,
        [
            new StewardStatisticsSection(text.StewardStatisticsTypes, [.. statistics.Types.Select(row => Row(TypeText(row.Type), null, Number(row.Count)))]),
            new StewardStatisticsSection(text.StewardStatisticsTags, Counts(statistics.Tags, name => name)),
            new StewardStatisticsSection(text.StewardStatisticsLicenses, Counts(statistics.Licenses, name => name)),
            new StewardStatisticsSection(text.StewardStatisticsReleaseHosts, Counts(statistics.ReleaseHosts, MainViewModel.DownloadHostName)),
            new StewardStatisticsSection(text.StewardStatisticsMostDownloaded, [.. statistics.MostDownloaded.Select(row =>
                Row(row.Name, row.Id, text.FormatContentDownloadsExact(row.Total.ToString("N0", CultureInfo.CurrentCulture))))]),
        ]);
    }

    private string TypeText(ContentType type) => type switch
    {
        ContentType.Mod => _owner.Localization.DiscoverTabMods,
        ContentType.ModLoader => _owner.Localization.DiscoverTabLoaders,
        ContentType.ModPack => _owner.Localization.DiscoverTabModpacks,
        ContentType.Vehicle => _owner.Localization.DiscoverTabVehicles,
        _ => _owner.Localization.DiscoverTabSaves,
    };

    private StewardStatisticsSection Listings(string title, IReadOnlyList<IndexStatistics.Listing> listings) =>
        new(title, [.. listings.Select(listing => Row(listing.Name, listing.Id, null))]);

    private List<StewardStatisticsRow> Counts(IReadOnlyList<IndexStatistics.NameCount> counts, Func<string, string> nameText) =>
        [.. counts.Select(count => Row(nameText(count.Name), null, Number(count.Count)))];

    private StewardStatisticsRow Row(string name, string? id, string? detail) => new(_owner, name, id, detail);

    private static string Number(int count) => count.ToString(CultureInfo.CurrentCulture);
}

/// <param name="Summary">One sentence under the heading, or null.</param>
public sealed record StewardStatisticsCard(string Heading, string? Summary, IReadOnlyList<StewardStatisticsSection> Sections);

public sealed record StewardStatisticsSection(string Title, IReadOnlyList<StewardStatisticsRow> Rows);

/// <summary>One line of a card. A row with the id of a listing or a pack opens its content page, and every other row shows only its text.</summary>
public sealed partial class StewardStatisticsRow
{
    private readonly MainViewModel _owner;
    private readonly string? _id;

    public StewardStatisticsRow(MainViewModel owner, string name, string? id, string? detail)
    {
        _owner = owner;
        _id = id;
        Name = name;
        Detail = detail;
    }

    public string Name { get; }

    public string? Detail { get; }

    public bool CanOpen => _id is not null;

    /// <summary>A click is a navigation, so it may start the Discover load, which can fetch the index and which a read of the tab must not start.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        await _owner.EnsureDiscoverLoadedAsync();
        var (listing, pack) = _owner.FindIndexContent(_id ?? string.Empty);
        if (listing is not null)
            await _owner.OpenContentAsync(listing);
        else if (pack is not null)
            await _owner.OpenPackAsync(pack);
    }
}
