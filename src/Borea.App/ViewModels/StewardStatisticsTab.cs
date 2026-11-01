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
/// The Statistics tab of the Steward page: the headline numbers, then what <see cref="IndexStatistics"/> counts as bars and tables, the most useful card first.
/// It reads the snapshot that Borea already holds, so a read of the tab makes no request.
/// </summary>
public sealed partial class StewardStatisticsTab : ObservableObject
{
    private const int FirstListRows = 6;

    private const int FirstUpkeepRows = 8;

    private const int FirstBlockedMods = 12;

    private const string NoValue = "-";

    /// <summary>From here a tile shows the short form, because the full number does not fit its width at the smallest window.</summary>
    private const long CompactFrom = 1_000_000;

    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;
    private IndexStatistics? _shown;

    public StewardStatisticsTab(MainViewModel owner)
    {
        _owner = owner;
    }

    [ObservableProperty]
    private StewardStatisticsReport? _report;

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
        Report = statistics is null ? null : ReportOf(statistics);
    }

    private StewardStatisticsReport ReportOf(IndexStatistics statistics)
    {
        var text = _owner.Localization;
        var upkeep = statistics.UpkeepRows();
        var mostDependencies = statistics.MostDependencies.Concat(statistics.MostDependedOn).Select(row => row.Required + row.Optional).DefaultIfEmpty().Max();
        return new StewardStatisticsReport(
            Tiles(statistics, upkeep.Count),
            BuildFit(statistics),
            Dependencies(text.StewardStatisticsMostDependencies, statistics.MostDependencies, mostDependencies),
            Dependencies(text.StewardStatisticsMostDependedOn, statistics.MostDependedOn, mostDependencies),
            Upkeep(statistics, upkeep),
            Bars(text.StewardStatisticsTypes, statistics.Types.Where(row => row.Count > 0).OrderByDescending(row => row.Count).Select(row => (TypeText(row.Type), (string?)null, (long)row.Count))),
            Bars(text.StewardStatisticsTags, Counts(statistics.Tags, name => name)),
            Bars(text.StewardStatisticsLicenses, Counts(statistics.Licenses, name => name)),
            Bars(text.StewardStatisticsReleaseHosts, Counts(statistics.ReleaseHosts, MainViewModel.DownloadHostName)),
            Bars(text.StewardStatisticsMostDownloaded, statistics.MostDownloaded.Select(row => (row.Name, (string?)row.Id, row.Total)), IndexStatistics.RankedRows));
    }

    private IReadOnlyList<StewardStatisticsTile> Tiles(IndexStatistics statistics, int upkeepListings)
    {
        var text = _owner.Localization;
        var notBlocked = statistics.ModsNotBlocked;
        var mods = notBlocked + statistics.ModsBlocked.Count;
        var isBuildRated = statistics.CurrentBuild is not null && mods > 0;
        return
        [
            new StewardStatisticsTile(Number(statistics.Types.Sum(row => row.Count)), text.StewardStatisticsTotalListings),
            new StewardStatisticsTile(isBuildRated ? Percent(notBlocked, mods) : NoValue, text.StewardStatisticsModsNotBlocked, BuildFitText(statistics), isBuildRated),
            new StewardStatisticsTile(Number(upkeepListings), text.StewardStatisticsUpkeepListings),
            new StewardStatisticsTile(statistics.TotalDownloads < CompactFrom ? Number(statistics.TotalDownloads) : MainViewModel.CompactCount(statistics.TotalDownloads), text.StewardStatisticsTotalDownloads, text.FormatContentDownloadsExact(Number(statistics.TotalDownloads))),
        ];
    }

    private StewardStatisticsBuildFit BuildFit(IndexStatistics statistics)
    {
        var text = _owner.Localization;
        var notBlocked = statistics.ModsNotBlocked;
        var blocked = statistics.ModsBlocked.Count;
        var mods = notBlocked + blocked;
        return new StewardStatisticsBuildFit(
            statistics.CurrentBuild?.ToString(),
            BuildFitText(statistics),
            new StewardStatisticsPart(Count(notBlocked), Percent(notBlocked, mods), Share(notBlocked, mods)),
            new StewardStatisticsPart(Count(blocked), Percent(blocked, mods), Share(blocked, mods)),
            new StewardStatisticsList(_owner, text.StewardStatisticsBlockedMods, Rows(statistics.ModsBlocked), FirstBlockedMods));
    }

    /// <summary>The sentence that says what the numbers of the build mean, or why there are none when the index names no build or lists no mods.</summary>
    private string BuildFitText(IndexStatistics statistics)
    {
        var text = _owner.Localization;
        var mods = statistics.ModsNotBlocked + statistics.ModsBlocked.Count;
        return statistics.CurrentBuild is not { } build ? text.StewardStatisticsNoBuild
            : mods == 0 ? text.StewardStatisticsNoMods
            : text.FormatStewardStatisticsNotBlocked(Number(statistics.ModsNotBlocked), Number(mods), build.ToString());
    }

    /// <param name="most">The largest total of both dependency tables, so one length means one count in either of them.</param>
    private StewardStatisticsList Dependencies(string title, IReadOnlyList<IndexStatistics.DependencyCount> rows, int most) =>
        new(_owner, title,
        [
            .. rows.Select(row => new StewardStatisticsDependencyRow(_owner, row.Name, row.IsListed ? row.Id : null, Count(row.Required), Count(row.Optional), Share(row.Required, most), Share(row.Optional, most))),
        ], FirstListRows);

    private StewardStatisticsUpkeep Upkeep(IndexStatistics statistics, IReadOnlyList<IndexStatistics.UpkeepRow> rows)
    {
        var text = _owner.Localization;
        var problems = new (string Title, int Count, Func<IndexStatistics.UpkeepRow, bool> HasProblem)[]
        {
            (text.StewardStatisticsNoCurrentRelease, statistics.NoCurrentRelease.Count, row => row.NoCurrentRelease),
            (text.FormatStewardStatisticsNoUpdate(Number(IndexStatistics.NoUpdateDays)), statistics.NoRecentUpdate.Count, row => row.NoRecentUpdate),
            (text.StewardStatisticsNoImage, statistics.NoImage.Count, row => row.NoImage),
            (text.StewardStatisticsNoForumsThread, statistics.NoForumsThread.Count, row => row.NoForumsThread),
        };

        // without a build nothing was rated, so a column for it would claim a zero that nobody checked
        var rated = statistics.CurrentBuild is null ? problems[1..] : problems;
        return new StewardStatisticsUpkeep(
            [.. rated.Select(problem => new StewardStatisticsUpkeepColumn(problem.Title, Count(problem.Count)))],
            new StewardStatisticsList(_owner, text.StewardStatisticsUpkeep,
            [
                .. rows.Select(row => new StewardStatisticsUpkeepRow(_owner, row.Name, row.Id, [.. rated.Select(problem => new StewardStatisticsUpkeepCell(problem.Title, problem.HasProblem(row)))])),
            ], FirstUpkeepRows));
    }

    private string TypeText(ContentType type) => type switch
    {
        ContentType.Mod => _owner.Localization.DiscoverTabMods,
        ContentType.ModLoader => _owner.Localization.DiscoverTabLoaders,
        ContentType.ModPack => _owner.Localization.DiscoverTabModpacks,
        ContentType.Vehicle => _owner.Localization.DiscoverTabVehicles,
        _ => _owner.Localization.DiscoverTabSaves,
    };

    /// <summary>The longest bar of a list fills its width and the others follow, because a bar here compares rows and is no share of a whole.</summary>
    private StewardStatisticsList Bars(string title, IEnumerable<(string Name, string? Id, long Count)> rows, int firstRows = FirstListRows)
    {
        var counted = rows.ToList();
        var most = counted.Select(row => row.Count).DefaultIfEmpty().Max();
        return new StewardStatisticsList(_owner, title, [.. counted.Select(row => new StewardStatisticsBarRow(_owner, row.Name, row.Id, Number(row.Count), Share(row.Count, most)))], firstRows);
    }

    private static IEnumerable<(string Name, string? Id, long Count)> Counts(IReadOnlyList<IndexStatistics.NameCount> counts, Func<string, string> nameText) =>
        counts.Select(count => (nameText(count.Name), (string?)null, (long)count.Count));

    private List<StewardStatisticsRow> Rows(IReadOnlyList<IndexStatistics.Listing> listings) =>
        [.. listings.Select(listing => new StewardStatisticsRow(_owner, listing.Name, listing.Id))];

    private static StewardStatisticsCount Count(int count) => new(count == 0 ? NoValue : Number(count), count == 0);

    private static double Share(long part, long whole) => whole <= 0 ? 0 : (double)part / whole;

    /// <summary>A part that is neither none nor all never rounds to 0 or 100 percent, so the text cannot claim that no mod or every mod is blocked.</summary>
    internal static string Percent(long part, long whole)
    {
        var percent = Math.Round(Share(part, whole) * 100);
        var shown = part == 0 || part == whole ? percent : Math.Clamp(percent, 1, 99);
        return (shown / 100).ToString("P0", CultureInfo.CurrentCulture);
    }

    private static string Number(long count) => count.ToString("N0", CultureInfo.CurrentCulture);
}

public sealed record StewardStatisticsReport(
    IReadOnlyList<StewardStatisticsTile> Tiles,
    StewardStatisticsBuildFit BuildFit,
    StewardStatisticsList MostDependencies,
    StewardStatisticsList MostDependedOn,
    StewardStatisticsUpkeep Upkeep,
    StewardStatisticsList Types,
    StewardStatisticsList Tags,
    StewardStatisticsList Licenses,
    StewardStatisticsList ReleaseHosts,
    StewardStatisticsList MostDownloaded);

/// <param name="Detail">The exact number or the sentence behind the value, for the tooltip.</param>
/// <param name="IsKnown">False when the snapshot does not give the value, so the tile shows a muted dash.</param>
public sealed record StewardStatisticsTile(string Value, string Label, string? Detail = null, bool IsKnown = true);

/// <param name="Build">The current build, or null when the index names none, so only <paramref name="Summary"/> says something.</param>
public sealed record StewardStatisticsBuildFit(string? Build, string Summary, StewardStatisticsPart NotBlocked, StewardStatisticsPart Blocked, StewardStatisticsList BlockedMods)
{
    public bool HasBuild => Build is not null;

    /// <summary>False without a build or without a mod, so there is no part to draw and <see cref="Summary"/> says why.</summary>
    public bool IsRated => HasBuild && NotBlocked.Share + Blocked.Share > 0;
}

/// <summary>One side of the bar of the current build.</summary>
public sealed record StewardStatisticsPart(StewardStatisticsCount Count, string Percent, double Share);

/// <summary>A count where a zero is noise, so it shows as a muted dash.</summary>
public sealed record StewardStatisticsCount(string Text, bool IsZero);

/// <param name="Columns">The problems the table rates. The cells of each row are in the same order.</param>
public sealed record StewardStatisticsUpkeep(IReadOnlyList<StewardStatisticsUpkeepColumn> Columns, StewardStatisticsList Rows);

public sealed record StewardStatisticsUpkeepColumn(string Title, StewardStatisticsCount Count);

public sealed record StewardStatisticsUpkeepCell(string Problem, bool HasProblem);

/// <summary>
/// The rows of a table or a ranked list.
/// A long one shows its first rows until Show all, so the page never needs a long scroll.
/// </summary>
public sealed partial class StewardStatisticsList : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly IReadOnlyList<StewardStatisticsRow> _rows;
    private readonly int _firstRows;

    public StewardStatisticsList(MainViewModel owner, string title, IReadOnlyList<StewardStatisticsRow> rows, int firstRows)
    {
        _owner = owner;
        _rows = rows;
        _firstRows = firstRows;
        Title = title;
    }

    public string Title { get; }

    public bool IsEmpty => _rows.Count == 0;

    public bool CanShowAll => _rows.Count > _firstRows;

    public IReadOnlyList<StewardStatisticsRow> ShownRows => ShowsAll ? _rows : [.. _rows.Take(_firstRows)];

    public string ShowAllText => ShowsAll
        ? _owner.Localization.StewardStatisticsShowFewer
        : _owner.Localization.FormatStewardStatisticsShowAll(_rows.Count.ToString("N0", CultureInfo.CurrentCulture));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownRows), nameof(ShowAllText))]
    private bool _showsAll;

    [RelayCommand]
    private void ToggleShowAll() => ShowsAll = !ShowsAll;
}

/// <summary>
/// One row of a list or a table.
/// A row with the id of a listing or a pack opens its content page, and every other row shows only its text.
/// </summary>
public partial class StewardStatisticsRow
{
    private readonly MainViewModel _owner;
    private readonly string? _id;

    public StewardStatisticsRow(MainViewModel owner, string name, string? id)
    {
        _owner = owner;
        _id = id;
        Name = name;
    }

    public string Name { get; }

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

public sealed class StewardStatisticsBarRow(MainViewModel owner, string name, string? id, string value, double share) : StewardStatisticsRow(owner, name, id)
{
    public string Value { get; } = value;

    public double Share { get; } = share;
}

public sealed class StewardStatisticsDependencyRow(MainViewModel owner, string name, string? id, StewardStatisticsCount required, StewardStatisticsCount optional, double requiredShare, double optionalShare) : StewardStatisticsRow(owner, name, id)
{
    public StewardStatisticsCount Required { get; } = required;

    public StewardStatisticsCount Optional { get; } = optional;

    public double RequiredShare { get; } = requiredShare;

    public double OptionalShare { get; } = optionalShare;
}

public sealed class StewardStatisticsUpkeepRow(MainViewModel owner, string name, string id, IReadOnlyList<StewardStatisticsUpkeepCell> cells) : StewardStatisticsRow(owner, name, id)
{
    public IReadOnlyList<StewardStatisticsUpkeepCell> Cells { get; } = cells;
}
