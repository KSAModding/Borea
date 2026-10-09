using System;
using System.Globalization;
using Borea.Core.Listings;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Steward page and the steward actions of the content and pack pages. Each action shows only to a steward of the index repository
/// that it changes, which only decides what Borea shows, because GitHub enforces the rights.
/// </summary>
public partial class MainViewModel
{
    private StewardPage? _stewardPage;

    [ObservableProperty]
    private bool _currentWindowSteward;

    /// <summary>The open confirmation of a change of index-status.toml, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStewardChangeOpen))]
    private IndexStatusDialog? _stewardChange;

    /// <summary>The open amendment of the release files of a listing, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStewardAmendmentOpen))]
    private ReleaseAmendmentDialog? _stewardAmendment;

    /// <summary>The open confirmation of an action on the pull request of the review, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStewardActionOpen))]
    private PullRequestActionDialog? _stewardAction;

    public StewardPage StewardPage => _stewardPage ??= new StewardPage(this);

    public bool IsStewardChangeOpen => StewardChange is not null;

    public bool IsStewardActionOpen => StewardAction is not null;

    public bool IsStewardAmendmentOpen => StewardAmendment is not null;

    /// <summary>Editing index-status.toml needs the bypass of content-index itself.</summary>
    public bool CanEditIndexStatus => IsGitHubSignedIn && _services?.StewardRole.Current is { ContentIndex: true };

    /// <summary>A listing of the content index, as opposed to one that only SpaceDock knows.</summary>
    public bool CanEditContentStatus => CanEditIndexStatus && SelectedContent?.Source == "index";

    /// <summary>Amending the release files of a listing of the content index needs the bypass of content-index-releases.</summary>
    public bool CanAmendContentReleases => IsStewardOf(StewardAccess.ReleasesRepository) && SelectedContent?.Source == "index";

    /// <summary>The actions on a pull request need the bypass of its own repository.</summary>
    internal bool IsStewardOf(string repository) =>
        IsGitHubSignedIn && _services?.StewardRole.Current is { } access
        && (string.Equals(repository, ListingPullRequestLinks.Repository, StringComparison.OrdinalIgnoreCase) ? access.ContentIndex
            : string.Equals(repository, StewardAccess.ReleasesRepository, StringComparison.OrdinalIgnoreCase) && access.ContentIndexReleases);

    private void RefreshIndexStatusRights()
    {
        OnPropertyChanged(nameof(CanEditIndexStatus));
        OnPropertyChanged(nameof(CanEditContentStatus));
        OnPropertyChanged(nameof(CanAmendContentReleases));
        _stewardPage?.Review?.RefreshRights();

        // a report says whether the account that is now signed in filed it or owns its listing
        _stewardPage?.Reports.RefreshRights();
    }

    [RelayCommand]
    private void OpenStewardPage()
    {
        if (!IsGitHubSteward)
            return;

        IsSettingsOpen = false;
        LeaveContentPage();
        LeavePackPage();
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        IsTasksOpen = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
        CurrentWindowSteward = true;
        StewardPage.CloseReview();
        _ = StewardPage.RefreshTabAsync();
    }

    [RelayCommand]
    private void DisputeContent()
    {
        if (CanEditContentStatus && SelectedContent is { } item)
            BeginIndexStatusChange(IndexStatusChange.Dispute(item.ModId, string.Empty));
    }

    [RelayCommand]
    private void DelistContent()
    {
        if (CanEditContentStatus && SelectedContent is { } item)
            BeginIndexStatusChange(IndexStatusChange.Delist(item.ModId, string.Empty));
    }

    [RelayCommand]
    private void DisputePack()
    {
        if (CanEditIndexStatus && SelectedPack is { } pack)
            BeginIndexStatusChange(IndexStatusChange.Dispute(pack.PackId, string.Empty));
    }

    [RelayCommand]
    private void DelistPack()
    {
        if (CanEditIndexStatus && SelectedPack is { } pack)
            BeginIndexStatusChange(IndexStatusChange.Delist(pack.PackId, string.Empty));
    }

    /// <summary>Retracts the pack version the page shows.</summary>
    [RelayCommand]
    private void RetractPackVersion()
    {
        if (CanEditIndexStatus && SelectedPack is { } pack)
            BeginIndexStatusChange(IndexStatusChange.Retract(pack.PackId, pack.Version, string.Empty));
    }

    /// <summary>Opens the amendment of the releases of the listing the page shows, and reads its releases. Another open amendment stays until it is closed.</summary>
    [RelayCommand]
    private void AmendContentReleases()
    {
        if (StewardAmendment is not null || !CanAmendContentReleases || SelectedContent is not { } item)
            return;

        var dialog = new ReleaseAmendmentDialog(this, item.ModId);
        StewardAmendment = dialog;
        dialog.Start();
    }

    internal void CloseReleaseAmendment(ReleaseAmendmentDialog dialog)
    {
        if (ReferenceEquals(StewardAmendment, dialog))
            StewardAmendment = null;
    }

    /// <summary>The queue shows the new pull request once the checks of content-index-releases label it.</summary>
    internal void OnReleaseAmendmentOpened()
    {
        if (StewardPage.Queue.IsLoaded)
            _ = StewardPage.Queue.RefreshAsync();
    }

    /// <summary>Opens the confirmation and checks the change against the base branch. Another open confirmation stays until it is closed.</summary>
    /// <param name="report">The report that the change answers, which its pull request names, or null.</param>
    internal void BeginIndexStatusChange(IndexStatusChange change, IndexReport? report = null)
    {
        if (StewardChange is not null || !CanEditIndexStatus)
            return;

        var dialog = new IndexStatusDialog(this, report is null ? change : change with { Report = report.Number }, report);
        StewardChange = dialog;
        dialog.Start();
    }

    internal void CloseIndexStatusChange(IndexStatusDialog dialog)
    {
        if (ReferenceEquals(StewardChange, dialog))
            StewardChange = null;
    }

    /// <summary>Opens the confirmation of the action on the pull request that the review shows. Another open confirmation stays until it is closed.</summary>
    internal void BeginPullRequestAction(StewardReview review, PullRequestAction action)
    {
        if (StewardAction is not null || review.PullRequest is not { } pullRequest || !review.CanAct || (action == PullRequestAction.Merge && !review.CanMerge))
            return;

        StewardAction = new PullRequestActionDialog(this, review, pullRequest, action);
    }

    internal void ClosePullRequestAction(PullRequestActionDialog dialog)
    {
        if (ReferenceEquals(StewardAction, dialog))
            StewardAction = null;
    }

    /// <summary>The Status tab shows the new pull request, and its list conflicts once one of them merges.</summary>
    internal void OnIndexStatusPullRequestOpened()
    {
        if (StewardPage.IsLoaded || (CurrentWindowSteward && StewardPage.IsStatusTab))
            _ = StewardPage.RefreshAsync();
    }

    private void LeaveStewardPage() => CurrentWindowSteward = false;

    /// <summary>Whether the signed-in account filed the report, which makes the steward a party to it.</summary>
    internal bool IsReporter(IndexReport report) =>
        GitHubLogin is { } login && string.Equals(login, report.Author, StringComparison.OrdinalIgnoreCase);

    internal string IndexStatusStateText(string state) => state switch
    {
        IndexStatusEntry.Delisted => Localization.StewardStateDelisted,
        IndexStatusEntry.Disputed => Localization.StewardStateDisputed,
        IndexStatusEntry.Retracted => Localization.StewardStateRetracted,
        _ => state,
    };

    internal string IndexStatusRefusalText(IndexStatusRefusal refusal, string id, string? version) => refusal switch
    {
        IndexStatusRefusal.InvalidReason => Localization.StewardRefusedInvalidReason,
        IndexStatusRefusal.MissingVersion => Localization.StewardRefusedMissingVersion,
        IndexStatusRefusal.Duplicate => Localization.FormatStewardRefusedDuplicate(id),
        IndexStatusRefusal.UnknownId => Localization.FormatStewardRefusedUnknownId(id),
        IndexStatusRefusal.NotAPack => Localization.FormatStewardRefusedNotAPack(id),
        IndexStatusRefusal.UnknownVersion => Localization.FormatStewardRefusedUnknownVersion(id, version ?? string.Empty),
        _ => Localization.StewardRefusedNotInFile,
    };

    internal string ReleaseAmendmentRefusalText(ReleaseAmendmentRefusal refusal) => refusal switch
    {
        ReleaseAmendmentRefusal.MonthNotOver => Localization.StewardAmendRefusedMonthNotOver,
        ReleaseAmendmentRefusal.UnknownMonth => Localization.StewardAmendRefusedUnknownMonth,
        ReleaseAmendmentRefusal.UnknownRelease => Localization.StewardAmendRefusedUnknownRelease,
        ReleaseAmendmentRefusal.NotStamperFile => Localization.StewardAmendRefusedNotStamperFile,
        ReleaseAmendmentRefusal.NoLoader => Localization.StewardAmendRefusedNoLoader,
        ReleaseAmendmentRefusal.NoDependency => Localization.StewardAmendRefusedNoDependency,
        ReleaseAmendmentRefusal.Widens => Localization.StewardAmendRefusedWidens,
        ReleaseAmendmentRefusal.OutsideClass => Localization.StewardAmendRefusedOutsideClass,
        ReleaseAmendmentRefusal.UnreadableArchive => Localization.StewardAmendRefusedUnreadableArchive,
        _ => Localization.StewardAmendRefusedInvalidChange,
    };

    /// <summary>Why the releases could not be read or amended. The errors of the status edits name content-index, so these name content-index-releases.</summary>
    internal string ReleaseAmendmentErrorText(StewardException exception) => exception.Failure switch
    {
        StewardFailure.NotSteward => Localization.StewardAmendErrorNotSteward,
        StewardFailure.NotFound => Localization.StewardAmendErrorNotFound,
        StewardFailure.Forbidden => Localization.StewardQueueForbidden,
        StewardFailure.UnreadableFile => Localization.FormatStewardAmendErrorUnreadable(exception.Detail ?? string.Empty).TrimEnd(),
        _ => StewardErrorText(exception),
    };

    internal string StewardQueueKindText(StewardQueueKind kind) => kind switch
    {
        StewardQueueKind.Listing => Localization.StewardQueueKindListing,
        StewardQueueKind.Pack => Localization.StewardQueueKindPack,
        StewardQueueKind.Release => Localization.StewardQueueKindRelease,
        StewardQueueKind.Amendment => Localization.StewardQueueKindAmendment,
        StewardQueueKind.OwnerRecord => Localization.StewardQueueKindOwnerRecord,
        StewardQueueKind.IndexStatus => Localization.StewardQueueKindIndexStatus,
        _ => Localization.StewardQueueKindTagVocabulary,
    };

    /// <summary>Why one repository of the queue could not be read. The errors of the status edits name content-index, so these name the repository.</summary>
    internal string StewardQueueFailureText(StewardQueueFailure failure) => Localization.FormatStewardQueueFailed(
        failure.Repository,
        failure.Error.Failure switch
        {
            StewardFailure.Forbidden => Localization.StewardQueueForbidden,
            StewardFailure.NotFound => Localization.StewardQueueNotFound,
            _ => StewardErrorText(failure.Error),
        });

    /// <summary>Why one repository of the Watcher tab could not be read.</summary>
    internal string StewardWatcherFailureText(WatcherIssuesFailure failure) => StewardIssuesFailureText(failure.Repository, failure.Error);

    /// <summary>Why the issues of a repository could not be read. They are read without the token, so a refusal is no missing App.</summary>
    internal string StewardIssuesFailureText(string repository, StewardException exception) => Localization.FormatStewardWatcherFailed(
        repository,
        exception.Failure switch
        {
            StewardFailure.Forbidden => Localization.StewardWatcherForbidden,
            StewardFailure.NotFound => Localization.StewardQueueNotFound,
            _ => StewardErrorText(exception),
        });

    /// <summary>Why a pull request could not be read or acted on. The errors of the status edits name content-index, so these name the pull request or its repository.</summary>
    internal string StewardPullRequestErrorText(StewardException exception) => exception.Failure switch
    {
        StewardFailure.Forbidden => Localization.StewardQueueForbidden,
        StewardFailure.NotFound => Localization.StewardReviewNotFound,
        _ => StewardErrorText(exception),
    };

    /// <summary>Why Borea does not merge, or does not act on the pull request as the review showed it.</summary>
    internal string PullRequestRefusalText(PullRequestRefusal refusal) => refusal switch
    {
        PullRequestRefusal.NotOpen => Localization.StewardActionNotOpen,
        PullRequestRefusal.Changed => Localization.StewardActionChanged,
        PullRequestRefusal.Draft => Localization.StewardMergeDraft,
        PullRequestRefusal.Validate => Localization.StewardMergeNeedsValidate,
        PullRequestRefusal.Verdict => Localization.StewardMergeNeedsVerdict,
        _ => Localization.StewardActionSkipReview,
    };

    /// <summary>The repository without its owner and the number, such as "content-index #5".</summary>
    internal static string RepositoryNumberText(string repository, int number) =>
        $"{repository[(repository.IndexOf('/', StringComparison.Ordinal) + 1)..]} #{number.ToString(CultureInfo.InvariantCulture)}";

    internal string StewardErrorText(StewardException exception) => exception.Failure switch
    {
        StewardFailure.SignedOut => Localization.StewardErrorSignedOut,
        StewardFailure.NotSteward => Localization.StewardErrorNotSteward,
        StewardFailure.RateLimited => Localization.FormatListingErrorRateLimit(
            (exception.RetryAt ?? DateTimeOffset.Now).ToLocalTime().ToString("t", CultureInfo.CurrentCulture)),
        StewardFailure.NotFound => Localization.StewardErrorNotFound,
        StewardFailure.Refused => Localization.FormatStewardErrorRefused(exception.Detail ?? string.Empty).TrimEnd(),
        StewardFailure.Forbidden => Localization.StewardErrorForbidden,
        StewardFailure.NetworkError => Localization.StewardErrorNetwork,
        StewardFailure.UnreadableFile => Localization.FormatStewardErrorUnreadable(exception.Detail ?? string.Empty).TrimEnd(),
        _ => Localization.StewardErrorUnexpected,
    };
}
