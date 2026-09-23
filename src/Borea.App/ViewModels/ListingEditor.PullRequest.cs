using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.Listings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The pull request that Borea opens from the signed-in GitHub account, the ownership pre-check before it,
/// and the verdict of the checks after it. The sign-in lasts while the page is open and the pull request is open.
/// </summary>
public sealed partial class ListingEditor
{
    private CancellationTokenSource? _publishing;
    private CancellationTokenSource? _ownershipCheck;
    private CancellationTokenSource? _following;
    private string? _ownershipKey;
    private ListingPullRequest? _pullRequest;
    private string? _forkName;
    private long? _forkId;
    private bool _isPageOpen;
    private bool _signOutAfterPublish;
    private int _refreshes;

    /// <summary>How long the release host has to stay unchanged before the ownership pre-check runs.</summary>
    internal TimeSpan OwnershipDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    internal TimeSpan FollowInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>The last ownership pre-check, which tests wait for.</summary>
    internal Task OwnershipCheck { get; private set; } = Task.CompletedTask;

    /// <summary>The loop that follows the pull request, which tests wait for.</summary>
    internal Task Following { get; private set; } = Task.CompletedTask;

    public bool CanSignIn => _owner.IsGitHubAccountAvailable;

    public bool IsSignedIn => CanSignIn && _owner.IsGitHubSignedIn;

    public bool NeedsSignIn => CanSignIn && !IsSignedIn;

    /// <summary>This build has no GitHub sign-in, so only the browser opens the pull request.</summary>
    public bool UsesBrowserOnly => !CanSignIn;

    public string? PublishText => _owner.GitHubLogin is { } login ? Localization.FormatListingPublishText(login) : CanSignIn ? Localization.ListingSignInText : null;

    public string PublishLabel => HasOpenPullRequest ? Localization.ListingUpdatePullRequest : Localization.ListingPublish;

    /// <summary>Signed out, publishing signs in first.</summary>
    public bool CanPublish => CanSignIn && CanOpenPullRequest && !IsPublishing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private bool _isPublishing;

    [ObservableProperty]
    private string? _publishProgress;

    [ObservableProperty]
    private string? _publishError;

    /// <summary>What the author still has to do on GitHub before Borea can write: <see cref="ListingPublishFailure.NoFork"/> or <see cref="ListingPublishFailure.AppNotOnFork"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasForkStep), nameof(ForkStepText), nameof(ForkStepLabel), nameof(CanCheckOwnershipAgain))]
    private ListingPublishFailure? _forkStep;

    public bool HasForkStep => ForkStep is not null;

    public string? ForkStepText => ForkStep switch
    {
        ListingPublishFailure.NoFork => Localization.ListingForkMissing,
        ListingPublishFailure.AppNotOnFork => Localization.FormatListingAppNotOnFork(_forkName ?? string.Empty),
        _ => null,
    };

    public string? ForkStepLabel => ForkStep switch
    {
        ListingPublishFailure.NoFork => Localization.ListingMakeFork,
        ListingPublishFailure.AppNotOnFork => Localization.ListingAllowOnFork,
        _ => null,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnership), nameof(IsOwnershipVerified), nameof(OwnershipText), nameof(OwnershipDetail), nameof(OwnershipFixUrl), nameof(OwnershipFixLabel), nameof(CanCheckOwnershipAgain))]
    private ListingOwnership? _ownership;

    [ObservableProperty]
    private bool _isCheckingOwnership;

    public bool HasOwnership => Ownership is not null;

    public bool IsOwnershipVerified => Ownership?.State == ListingOwnershipState.Verified;

    /// <summary>A fork step has its own Check again, which runs the whole publish.</summary>
    public bool CanCheckOwnershipAgain => Ownership is { State: not ListingOwnershipState.Verified } && !HasForkStep;

    public string? OwnershipText => Ownership?.State switch
    {
        ListingOwnershipState.Verified => Localization.ListingOwnershipVerified,
        ListingOwnershipState.NotVerified => Localization.ListingOwnershipSteward,
        ListingOwnershipState.CouldNotEvaluate => Localization.ListingOwnershipUnknown,
        _ => null,
    };

    /// <summary>The proof the checks find, or the one step that gives one.</summary>
    public string? OwnershipDetail
    {
        get
        {
            if (Ownership is not { } ownership || _owner.GitHubLogin is not { } login)
                return null;

            var repository = ownership.Repository ?? string.Empty;
            if (IsSpaceDockLinkProblem(ownership))
                return Localization.FormatListingFixSpaceDockLink(ownership.SpaceDockMod!);

            if (ownership.State == ListingOwnershipState.Verified)
            {
                return ownership.Proof switch
                {
                    ListingOwnershipProof.Owner => Localization.FormatListingProofOwner(repository),
                    ListingOwnershipProof.Topic => Localization.FormatListingProofTopic(repository, ListingOwnership.TopicFor(login)),
                    ListingOwnershipProof.MarkerFile => Localization.FormatListingProofMarker(repository),
                    _ => null,
                };
            }

            return ownership.Problem switch
            {
                ListingOwnershipProblem.NoProof => Localization.FormatListingFixTopic(repository, ListingOwnership.TopicFor(login)),
                ListingOwnershipProblem.NoHost => Localization.ListingFixNoHost,
                ListingOwnershipProblem.RepositoryMissing => Localization.FormatListingFixMissing(repository),
                ListingOwnershipProblem.RepositoryFork => Localization.FormatListingFixFork(repository),
                ListingOwnershipProblem.RepositoryRenamed => Localization.FormatListingFixRenamed(repository, ownership.RenamedTo ?? string.Empty),
                ListingOwnershipProblem.SpaceDockModUnusable => Localization.FormatListingFixSpaceDockMod(ownership.SpaceDockMod ?? string.Empty),
                ListingOwnershipProblem.SpaceDockNoSourceLink => Localization.FormatListingFixSpaceDockLink(ownership.SpaceDockMod ?? string.Empty),
                ListingOwnershipProblem.PullRequestHasOtherFiles => Localization.FormatListingFixOtherFiles(ownership.PullRequest?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                _ => null,
            };
        }
    }

    public string? OwnershipFixUrl => Ownership switch
    {
        { SpaceDockMod: { } mod } ownership when IsSpaceDockLinkProblem(ownership) => "https://spacedock.info/mod/" + mod,
        { Problem: ListingOwnershipProblem.NoProof, Repository: { } repository } => "https://github.com/" + repository,
        { Problem: ListingOwnershipProblem.PullRequestHasOtherFiles, PullRequest: { } number } =>
            $"https://github.com/{ListingPullRequestLinks.Repository}/pull/{number.ToString(CultureInfo.InvariantCulture)}",
        _ => null,
    };

    public string? OwnershipFixLabel => Ownership switch
    {
        { } ownership when IsSpaceDockLinkProblem(ownership) => Localization.ListingOpenSpaceDock,
        { Problem: ListingOwnershipProblem.NoProof } => Localization.ListingOpenRepository,
        { Problem: ListingOwnershipProblem.PullRequestHasOtherFiles } => Localization.ListingOpenYourPullRequest,
        _ => null,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PullRequestStateText), nameof(PullRequestVerdict), nameof(IsPullRequestGood), nameof(IsPullRequestBad), nameof(HasOpenPullRequest), nameof(NeedsSignInToFollow), nameof(PublishLabel))]
    private ListingPullRequestStatus? _pullRequestStatus;

    [ObservableProperty]
    private string? _statusError;

    [ObservableProperty]
    private bool _isRefreshingStatus;

    public bool HasPullRequest => _pullRequest is not null;

    /// <summary>A pull request that the next Open pull request adds a commit to.</summary>
    public bool HasOpenPullRequest => _pullRequest is not null && PullRequestStatus?.IsFinal != true;

    public bool NeedsSignInToFollow => HasOpenPullRequest && NeedsSignIn;

    public string? PullRequestTitle => _pullRequest is { } pullRequest ? Localization.FormatListingPullRequest(Number(pullRequest)) : null;

    public string? PullRequestStateText => PullRequestStatus?.State switch
    {
        ListingPullRequestState.ChecksRunning => Localization.ListingStateRunning,
        ListingPullRequestState.ValidatedMerging => Localization.ListingStateMerging,
        ListingPullRequestState.WaitingForSteward => Localization.ListingStateSteward,
        ListingPullRequestState.Rejected => Localization.ListingStateRejected,
        ListingPullRequestState.CouldNotEvaluate => Localization.ListingStateNoVerdict,
        ListingPullRequestState.Merged => Localization.ListingStateMerged,
        ListingPullRequestState.Closed => Localization.ListingStateClosed,
        _ => null,
    };

    /// <summary>The comment of the checks, while it describes the head of the pull request.</summary>
    public string? PullRequestVerdict => PullRequestStatus is { State: not (ListingPullRequestState.ChecksRunning or ListingPullRequestState.Merged or ListingPullRequestState.Closed) } status
        ? status.Verdict
        : null;

    public bool IsPullRequestGood => PullRequestStatus?.State is ListingPullRequestState.ValidatedMerging or ListingPullRequestState.Merged;

    public bool IsPullRequestBad => PullRequestStatus?.State is ListingPullRequestState.Rejected or ListingPullRequestState.CouldNotEvaluate or ListingPullRequestState.Closed;

    /// <summary>
    /// Signs in when needed, finds the author's fork, commits the file on its own branch and opens the pull request,
    /// or adds a commit to the author's open one. Without a usable fork it shows the missing step instead.
    /// </summary>
    [RelayCommand]
    private async Task PublishAsync()
    {
        if (_owner.Services is not { } services || !CanPublish)
            return;

        PublishError = null;
        if (!IsSignedIn && !await _owner.SignInToGitHubAsync())
            return;

        var submission = new ListingSubmission(Draft.Id, Draft.Name, DocumentText, IsEdit);
        using var cancel = new CancellationTokenSource();
        _publishing = cancel;
        OutputMessage = null;
        ForkStep = null;
        PublishProgress = StepText(ListingPublishStep.Fork);
        IsPublishing = true;
        var progress = new Progress<ListingPublishStep>(step =>
        {
            if (ReferenceEquals(_publishing, cancel))
                PublishProgress = StepText(step);
        });

        try
        {
            var pullRequest = await services.ListingPublisher.PublishAsync(submission, progress, cancel.Token);
            if (!ReferenceEquals(_publishing, cancel))
                return;

            var number = Number(pullRequest);
            OutputMessage = pullRequest.Outcome switch
            {
                ListingPublishOutcome.Updated => Localization.FormatListingUpdated(number),
                ListingPublishOutcome.Unchanged => Localization.FormatListingUnchanged(number),
                _ => Localization.FormatListingOpened(number),
            };
            Follow(pullRequest);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (ListingPublishException exception) when (ReferenceEquals(_publishing, cancel) && exception.Failure is ListingPublishFailure.NoFork or ListingPublishFailure.AppNotOnFork)
        {
            _forkName = exception.Detail;
            _forkId = exception.RepositoryId;
            ForkStep = exception.Failure;
        }
        catch (ListingPublishException exception) when (ReferenceEquals(_publishing, cancel))
        {
            PublishError = ErrorText(exception);
        }
        catch (ListingPublishException)
        {
        }
        finally
        {
            if (ReferenceEquals(_publishing, cancel))
                _publishing = null;

            IsPublishing = false;
            PublishProgress = null;
            if (_signOutAfterPublish)
            {
                _signOutAfterPublish = false;
                services.GitHub.SignOut();
            }
        }
    }

    [RelayCommand]
    private void CancelPublish() => _publishing?.Cancel();

    [RelayCommand]
    private Task RefreshStatusAsync() => RefreshStatusCoreAsync(CancellationToken.None);

    [RelayCommand]
    private void OpenFollowedPullRequest()
    {
        if (_pullRequest is { } pullRequest && _owner.OpenListingPage(pullRequest.Url.AbsoluteUri) is { } error)
            StatusError = Localization.FormatListingOpenFailed(error);
    }

    [RelayCommand]
    private void CheckOwnershipAgain()
    {
        _ownershipKey = null;
        ScheduleOwnershipCheck();
    }

    [RelayCommand]
    private void OpenOwnershipFix()
    {
        if (OwnershipFixUrl is { } url && _owner.OpenListingPage(url) is { } error)
            PublishError = Localization.FormatListingOpenFailed(error);
    }

    [RelayCommand]
    private void OpenForkStep()
    {
        var url = ForkStep switch
        {
            ListingPublishFailure.NoFork => $"https://github.com/{ListingPullRequestLinks.Repository}/fork",
            ListingPublishFailure.AppNotOnFork => _forkId is { } fork ? _owner.Services?.GitHub.InstallUrlFor(fork) : _owner.Services?.GitHub.InstallUrl,
            _ => null,
        };
        if (url is not null && _owner.OpenListingPage(url) is { } error)
            PublishError = Localization.FormatListingOpenFailed(error);
    }

    private void Enter()
    {
        _isPageOpen = true;
        _signOutAfterPublish = false;
        StartFollowing();
    }

    /// <summary>
    /// Stops what runs only while the page is open, and signs out of GitHub, after a running publish.
    /// <see cref="OpenAsync"/> starts it again.
    /// </summary>
    internal void Leave()
    {
        Cancel();
        StopFollowing();
        StopOwnershipCheck();
        if (!_isPageOpen)
            return;

        _isPageOpen = false;
        if (_publishing is null)
            _owner.Services?.GitHub.SignOut();
        else
            _signOutAfterPublish = true;
    }

    /// <summary>Runs the ownership pre-check again when the account, the id or a release host changed.</summary>
    private void ScheduleOwnershipCheck()
    {
        if (!IsSignedIn || !IsFormStep || !_owner.CurrentWindowListing || _owner.Services is null)
        {
            StopOwnershipCheck();
            return;
        }

        var listed = IsEdit ? _base : null;
        var key = string.Join('|', _owner.GitHubLogin, Draft.Id, ListingAuthority.Of(Draft), listed is null ? null : ListingAuthority.Of(listed));
        if (key == _ownershipKey)
            return;

        _ownershipCheck?.Cancel();
        _ownershipKey = key;
        _ownershipCheck = new CancellationTokenSource();
        OwnershipCheck = CheckOwnershipAsync(Draft, listed, _ownershipCheck.Token);
    }

    private async Task CheckOwnershipAsync(ListingDraft submitted, ListingDraft? listed, CancellationToken cancellationToken)
    {
        if (_owner.Services is not { } services)
            return;

        Ownership = null;
        IsCheckingOwnership = true;
        try
        {
            await Task.Delay(OwnershipDelay, cancellationToken);
            var ownership = await services.ListingPublisher.CheckOwnershipAsync(submitted, listed, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                Ownership = ownership;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ListingPublishException)
        {
            // Only a sign-out gets here, and signed out the page hides the check.
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsCheckingOwnership = false;
        }
    }

    private void StopOwnershipCheck()
    {
        _ownershipCheck?.Cancel();
        _ownershipCheck = null;
        _ownershipKey = null;
        Ownership = null;
        IsCheckingOwnership = false;
    }

    private void Follow(ListingPullRequest pullRequest)
    {
        StopFollowing();
        _pullRequest = pullRequest;
        PullRequestStatus = null;
        StatusError = null;
        OnPullRequestChanged();
        StartFollowing();
    }

    /// <summary>Drops the pull request of the draft before, and the result of its publish, because they belong to another listing.</summary>
    private void ForgetPullRequest()
    {
        _publishing?.Cancel();
        _publishing = null;
        StopFollowing();
        _pullRequest = null;
        PullRequestStatus = null;
        StatusError = null;
        PublishError = null;
        ForkStep = null;
        OnPullRequestChanged();
    }

    private void OnPullRequestChanged()
    {
        OnPropertyChanged(nameof(HasPullRequest));
        OnPropertyChanged(nameof(HasOpenPullRequest));
        OnPropertyChanged(nameof(NeedsSignInToFollow));
        OnPropertyChanged(nameof(PullRequestTitle));
        OnPropertyChanged(nameof(PublishLabel));
    }

    private void StartFollowing()
    {
        if (_pullRequest is null || PullRequestStatus?.IsFinal == true || !IsSignedIn || !_owner.CurrentWindowListing)
            return;

        _following?.Cancel();
        _following = new CancellationTokenSource();
        Following = FollowAsync(_following.Token);
    }

    private void StopFollowing()
    {
        _following?.Cancel();
        _following = null;
    }

    private async Task FollowAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshStatusCoreAsync(cancellationToken);
                if (PullRequestStatus?.IsFinal == true || !IsSignedIn)
                    return;

                await Task.Delay(FollowInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshStatusCoreAsync(CancellationToken cancellationToken)
    {
        if (_owner.Services is not { } services || _pullRequest is not { } pullRequest)
            return;

        _refreshes++;
        IsRefreshingStatus = true;
        try
        {
            var status = await services.ListingPublisher.GetStatusAsync(pullRequest.Number, cancellationToken);
            if (!ReferenceEquals(pullRequest, _pullRequest))
                return;

            var wasFinal = PullRequestStatus?.IsFinal == true;
            PullRequestStatus = status;
            StatusError = null;
            if (status.IsFinal && !wasFinal)
                services.GitHub.SignOut();
        }
        catch (ListingPublishException exception) when (ReferenceEquals(pullRequest, _pullRequest))
        {
            StatusError = exception.Failure == ListingPublishFailure.SignedOut ? Localization.ListingFollowSignedOut : ErrorText(exception);
        }
        catch (ListingPublishException)
        {
        }
        finally
        {
            IsRefreshingStatus = --_refreshes > 0;
        }
    }

    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsGitHubSignedIn) && e.PropertyName != nameof(MainViewModel.IsGitHubAccountAvailable))
            return;

        OnPropertyChanged(nameof(CanSignIn));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(NeedsSignIn));
        OnPropertyChanged(nameof(NeedsSignInToFollow));
        OnPropertyChanged(nameof(UsesBrowserOnly));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(PublishText));
        ScheduleOwnershipCheck();
        FindListed();
        ForkStep = null;
        if (!IsSignedIn)
        {
            StopFollowing();
            if (HasOpenPullRequest)
                StatusError = Localization.ListingFollowSignedOut;
        }
        else if (_following is null)
        {
            StatusError = null;
            StartFollowing();
        }
    }

    private void RefreshPullRequestText()
    {
        OnPropertyChanged(nameof(PublishText));
        OnPropertyChanged(nameof(PublishLabel));
        OnPropertyChanged(nameof(OwnershipText));
        OnPropertyChanged(nameof(OwnershipDetail));
        OnPropertyChanged(nameof(OwnershipFixLabel));
        OnPropertyChanged(nameof(ForkStepText));
        OnPropertyChanged(nameof(ForkStepLabel));
        OnPropertyChanged(nameof(PullRequestTitle));
        OnPropertyChanged(nameof(PullRequestStateText));
    }

    private string StepText(ListingPublishStep step) => step switch
    {
        ListingPublishStep.Fork => Localization.ListingStepFork,
        ListingPublishStep.Branch => Localization.ListingStepBranch,
        ListingPublishStep.Commit => Localization.ListingStepCommit,
        ListingPublishStep.PullRequest => Localization.ListingStepPullRequest,
        _ => Localization.ListingStepFindPullRequest,
    };

    private string ErrorText(ListingPublishException exception)
    {
        var what = exception.Step switch
        {
            ListingPublishStep.FindPullRequest => Localization.ListingWhatPullRequests,
            ListingPublishStep.Fork => Localization.ListingWhatFork,
            ListingPublishStep.Branch => Localization.ListingWhatBranch,
            ListingPublishStep.Commit => Localization.ListingWhatFile,
            ListingPublishStep.PullRequest => Localization.ListingWhatPullRequest,
            _ => Localization.ListingWhatStatus,
        };

        return exception.Failure switch
        {
            ListingPublishFailure.SignedOut => Localization.ListingErrorSignedOut,
            ListingPublishFailure.RateLimited => Localization.FormatListingErrorRateLimit(
                (exception.RetryAt ?? DateTimeOffset.Now).ToLocalTime().ToString("t", CultureInfo.CurrentCulture)),
            ListingPublishFailure.NotFound => Localization.FormatListingErrorNotFound(what),
            ListingPublishFailure.Refused => Localization.FormatListingErrorRefused(what, exception.Detail ?? string.Empty).TrimEnd(),
            ListingPublishFailure.Forbidden => Localization.FormatListingErrorForbidden(what),
            ListingPublishFailure.NetworkError => Localization.ListingErrorNetwork,
            ListingPublishFailure.NoChange => Localization.ListingErrorNoChange,
            ListingPublishFailure.PullRequestNotOnFork => Localization.FormatListingErrorPullRequestNotOnFork(exception.Detail ?? string.Empty),
            _ => Localization.FormatListingErrorUnexpected(what),
        };
    }

    private static bool IsSpaceDockLinkProblem(ListingOwnership ownership) =>
        ownership.SpaceDockMod is not null
        && ownership.Problem is ListingOwnershipProblem.RepositoryMissing or ListingOwnershipProblem.RepositoryFork or ListingOwnershipProblem.RepositoryRenamed;

    private static string Number(ListingPullRequest pullRequest) => pullRequest.Number.ToString(CultureInfo.InvariantCulture);
}
