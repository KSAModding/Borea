using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Core.History;
using Borea.Core.Index;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// How the last refresh of the content index went, for Discover, About and the diagnostics.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndexStale))]
    [NotifyPropertyChangedFor(nameof(IsIndexUnreachable))]
    private ContentIndexRefreshStatus? _indexRefreshStatus;

    public bool IsIndexStale => IndexRefreshStatus is { Outcome: ContentIndexRefreshOutcome.Failed, CachedAt: not null };

    public bool IsIndexUnreachable => IndexRefreshStatus is { Outcome: ContentIndexRefreshOutcome.Failed, CachedAt: null };

    public string? IndexStaleText => IndexRefreshStatus is { Outcome: ContentIndexRefreshOutcome.Failed, CachedAt: { } cachedAt }
        ? Localization.FormatDiscoverIndexStale(Localization.FormatTimeAgo(DateTimeOffset.UtcNow - cachedAt))
        : null;

    public string? IndexUpdatedText => IndexRefreshStatus switch
    {
        null => null,
        { CachedAt: { } cachedAt } => Localization.FormatAboutIndexUpdated(Localization.FormatTimeAgo(DateTimeOffset.UtcNow - cachedAt)),
        _ => Localization.AboutIndexNotDownloaded,
    };

    public string? IndexFailureText => IndexRefreshStatus is { Outcome: ContentIndexRefreshOutcome.Failed } status
        ? Localization.FormatIndexUnreachable(status.FailureReason ?? string.Empty)
        : null;

    /// <summary>The clock the background check reads, so that a test can move it forward.</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>When Borea last asked the content index for a snapshot, whether or not that attempt succeeded.</summary>
    private DateTimeOffset? _lastIndexCheckAt;

    private Task _indexCheck = Task.CompletedTask;

    /// <summary>
    /// A caller that wants a toast sets this, and the check that is running
    /// when the fetch ends reads and clears it. A Refresh click that joins a
    /// check that started without a toast still gets its answer.
    /// </summary>
    private bool _showIndexCheckResult;

    /// <summary>Completes when a running content index check has finished.</summary>
    internal Task WhenIndexCheckedAsync() => _indexCheck;

    /// <summary>
    /// The Refresh action of Discover. It fetches the index, builds the pages
    /// from the new snapshot, and says in a toast what the refresh found.
    /// </summary>
    [RelayCommand]
    private Task RefreshContentIndexAsync() => StartIndexCheckAsync(showResult: true);

    /// <summary>
    /// "Try again" on Discover. A list that is already shown loads again only
    /// after a download, so a failed retry keeps the selected filters.
    /// </summary>
    [RelayCommand]
    private Task RetryContentIndexAsync() => StartIndexCheckAsync(showResult: false);

    /// <summary>
    /// Checks the index again when the last check is older than the
    /// revalidation interval. Discover and Library start it when they open, so
    /// a Borea that stays open still sees new mods and new releases.
    /// </summary>
    private void StartIndexCheck()
    {
        if (_services is not { } services || !_indexRefreshed)
            return;

        if (_lastIndexCheckAt is { } last && Clock.GetUtcNow() - last < services.IndexRefresh.RevalidationInterval)
            return;

        _ = StartIndexCheckAsync(showResult: false);
    }

    /// <summary>One check at a time, so a page that opens during a check joins it instead of starting a second one.</summary>
    private Task StartIndexCheckAsync(bool showResult)
    {
        if (_services is null)
            return Task.CompletedTask;

        _showIndexCheckResult |= showResult;
        if (!_indexCheck.IsCompleted)
            return _indexCheck;

        return _indexCheck = CheckContentIndexAsync(reloadPages: true);
    }

    /// <param name="reloadPages">False during the start, where no page is built yet.</param>
    private async Task CheckContentIndexAsync(bool reloadPages)
    {
        var outcome = await RunIndexRefreshAsync();

        // read once the fetch is over, so that a Refresh click during the check still gets its toast
        var showResult = _showIndexCheckResult;
        _showIndexCheckResult = false;

        // an unchanged index leaves the pages as they are, unless Discover has no list yet
        if (reloadPages && (outcome == ContentIndexRefreshOutcome.Downloaded || _discoverLoad is null))
            await ReloadFromIndexAsync();

        if (showResult && outcome is ContentIndexRefreshOutcome.Downloaded or ContentIndexRefreshOutcome.NotModified)
            Toasts.ShowMessage(ToastKind.Success, () => outcome == ContentIndexRefreshOutcome.Downloaded
                ? Localization.ToastIndexRefreshed
                : Localization.ToastIndexUpToDate);
    }

    /// <summary>
    /// Fetches the index and reports how it went. The refresh shows in the
    /// Tasks drawer, and a failure keeps the cached snapshot in use.
    /// </summary>
    private async Task<ContentIndexRefreshOutcome?> RunIndexRefreshAsync()
    {
        if (_services is not { } services)
            return null;

        _lastIndexCheckAt = Clock.GetUtcNow();
        var task = StartTask(TaskKind.IndexRefresh);
        var completed = false;
        string? error = null;
        try
        {
            await services.IndexRefresh.RefreshAsync();
            completed = true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            error = exception.Message;
        }
        finally
        {
            if (services.IndexRefresh.Status is { Outcome: ContentIndexRefreshOutcome.Failed } status)
                error = status.FailureReason ?? error ?? string.Empty;
            EndTask(task, completed, stopped: false, error);
        }

        UpdateIndexRefreshStatus();
        return error is null ? services.IndexRefresh.Status.Outcome : ContentIndexRefreshOutcome.Failed;
    }

    /// <summary>Builds the Home grid, the Discover list, the open mod page and the update counts from the snapshot.</summary>
    private async Task ReloadFromIndexAsync()
    {
        await LoadRecentItemsAsync();
        _discoverLoad = null;
        await EnsureDiscoverLoadedAsync();
        await ReloadContentPageAsync();
        StartContentUpdateCheck();
    }

    private void UpdateIndexRefreshStatus()
    {
        IndexRefreshStatus = _services?.IndexRefresh.Status;
        RefreshIndexStatusText();
    }

    /// <summary>The age in the texts grows, so they refresh even when the status stays equal.</summary>
    private void RefreshIndexStatusText()
    {
        OnPropertyChanged(nameof(IndexStaleText));
        OnPropertyChanged(nameof(IndexUpdatedText));
        OnPropertyChanged(nameof(IndexFailureText));
    }

    /// <summary>An absolute UTC time, because a bug report is read later.</summary>
    private static string IndexDiagnosticsLine(ContentIndexRefreshStatus status)
    {
        var cache = status.CachedAt is { } cachedAt
            ? $"updated {cachedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC"
            : "not downloaded";

        return status.Outcome == ContentIndexRefreshOutcome.Failed
            ? $"Content index: {cache}, the last refresh failed: {status.FailureReason}"
            : $"Content index: {cache}";
    }
}
