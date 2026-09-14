using System;
using System.Globalization;
using System.Threading.Tasks;
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

    /// <summary>
    /// "Try again" on Discover. A list that is already shown loads again only
    /// after a download, so a failed retry keeps the selected filters.
    /// </summary>
    [RelayCommand]
    private async Task RetryContentIndexAsync()
    {
        if (_services is null)
            return;

        _indexRefreshed = false;
        await RefreshContentIndexAsync();
        UpdateIndexRefreshStatus();
        if (_discoverLoad is not null && IndexRefreshStatus?.Outcome != ContentIndexRefreshOutcome.Downloaded)
            return;

        await LoadRecentItemsAsync();
        _discoverLoad = null;
        await EnsureDiscoverLoadedAsync();
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
