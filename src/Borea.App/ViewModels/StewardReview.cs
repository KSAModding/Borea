using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The review of one pull request on the Steward page, with the steward actions on it. Refresh reads the pull request again,
/// so a new commit shows with its own files, documents and status.
/// </summary>
public sealed partial class StewardReview : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly StewardQueueItem _item;
    private Task _load = Task.CompletedTask;

    public StewardReview(MainViewModel owner, StewardQueueItem item)
    {
        _owner = owner;
        _item = item;
    }

    /// <summary>The last read, or null before the first one ends.</summary>
    [ObservableProperty]
    private PullRequestReview? _pullRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    [NotifyPropertyChangedFor(nameof(MergeBlockedText))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    public ObservableCollection<StewardReviewFile> Files { get; } = [];

    public ObservableCollection<StewardReviewDocument> Documents { get; } = [];

    public string NumberText => MainViewModel.RepositoryNumberText(_item.Repository, _item.Number);

    public string Title => PullRequest?.Title ?? _item.Title;

    public Uri Url => PullRequest?.Url ?? _item.Url;

    public string? ByText => (PullRequest is { } pull ? pull.Author : _item.Author) is { } author ? _owner.Localization.FormatContentByAuthor(author) : null;

    public string? FromText => PullRequest is not { IsFromFork: true } pull ? null
        : pull.HeadRepository is { } head ? _owner.Localization.FormatStewardReviewFrom(head)
        : _owner.Localization.StewardReviewFromGone;

    public string? StateText => PullRequest?.State switch
    {
        PullRequestState.Closed => _owner.Localization.StewardReviewClosed,
        PullRequestState.Merged => _owner.Localization.StewardReviewMerged,
        _ => null,
    };

    public bool IsDraft => PullRequest?.IsDraft == true;

    public IReadOnlyList<string> Labels => PullRequest?.Labels ?? [];

    public string? CommitText => PullRequest is { } pull ? _owner.Localization.FormatStewardReviewCommit(pull.HeadCommit[..7]) : null;

    public string? ValidateText => PullRequest switch
    {
        { Validate: { } validate } => _owner.Localization.FormatStewardReviewValidate(validate.State switch
        {
            ValidateState.Success => _owner.Localization.StewardReviewValidateSuccess,
            ValidateState.Failure => _owner.Localization.StewardReviewValidateFailure,
            ValidateState.Error => _owner.Localization.StewardReviewValidateError,
            ValidateState.Pending => _owner.Localization.StewardReviewValidatePending,
            ValidateState.Missing => _owner.Localization.StewardReviewValidateMissing,
            _ => _owner.Localization.StewardReviewValidateUnknown,
        }),
        { ValidateFailure: { } failure } => _owner.Localization.FormatStewardReviewValidateUnreadable(failure.Failure switch
        {
            StewardFailure.Forbidden => _owner.Localization.StewardWatcherForbidden,

            // GitHub answers 404 or 422 for a commit it does not have, and the texts of the status edits name index-status.toml
            StewardFailure.NotFound or StewardFailure.Refused => _owner.Localization.StewardReviewCommitNotFound,
            _ => _owner.StewardErrorText(failure),
        }),
        _ => null,
    };

    public bool IsValidateSuccess => PullRequest?.Validate?.State == ValidateState.Success;

    public bool IsValidateBad => PullRequest is { Validate.State: ValidateState.Failure or ValidateState.Error } or { ValidateFailure: not null };

    public string? ValidateDescription => PullRequest?.Validate?.Description;

    public Uri? ValidateDetails => PullRequest?.Validate?.Details;

    public string? Verdict => PullRequest?.Verdict;

    public bool HasVerdict => Verdict is not null;

    public bool CanRunChecks => PullRequest?.CanRunChecks == true;

    public Uri ChecksWorkflowUrl => PullRequestReview.ChecksWorkflowUrl;

    public string FilesHeading => _owner.Localization.FormatStewardReviewFiles(Files.Count.ToString(CultureInfo.CurrentCulture));

    public bool HasDocuments => Documents.Count > 0;

    /// <summary>The actions show for an open pull request of a repository whose ruleset the account bypasses.</summary>
    public bool CanAct => !IsLoading && PullRequest is { State: PullRequestState.Open } pull && _owner.IsStewardOf(pull.Repository);

    public bool CanMerge => CanAct && PullRequestMerge.BlockersOf(PullRequest!).Count == 0;

    /// <summary>Why Merge is off, such as a validate that has not passed or a verdict that is missing.</summary>
    public string? MergeBlockedText => CanAct && PullRequestMerge.BlockersOf(PullRequest!) is { Count: > 0 } blockers
        ? string.Join(" ", blockers.Select(_owner.PullRequestRefusalText))
        : null;

    /// <summary>The signed-in steward opened the pull request, and POLICY.md asks a party to leave the case to another steward.</summary>
    public string? OwnWarning => PullRequest?.Author is { } author && string.Equals(author, _owner.GitHubLogin, StringComparison.OrdinalIgnoreCase)
        ? _owner.Localization.StewardActionOwnWarning
        : null;

    internal Task WhenLoadedAsync() => _load;

    /// <summary>Reads the pull request again. A second call during a read joins it.</summary>
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    /// <summary>Builds the texts of the review again in the language that is now selected.</summary>
    internal void RefreshText() => Show(PullRequest);

    /// <summary>Shows or hides the actions again after a sign-in, a sign-out or a new steward role, because they depend on the account.</summary>
    internal void RefreshRights()
    {
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(MergeBlockedText));
        OnPropertyChanged(nameof(OwnWarning));
    }

    [RelayCommand]
    private void Open() => Error = _owner.TryOpenWithSystem(Url.AbsoluteUri) ?? Error;

    [RelayCommand]
    private void RunChecks() => Error = _owner.TryOpenWithSystem(ChecksWorkflowUrl.AbsoluteUri) ?? Error;

    [RelayCommand]
    private void OpenValidateDetails()
    {
        if (ValidateDetails is { } details)
            Error = _owner.TryOpenWithSystem(details.AbsoluteUri) ?? Error;
    }

    internal void OpenLink(string url) => Error = _owner.TryOpenWithSystem(url) ?? Error;

    [RelayCommand]
    private void Approve() => _owner.BeginPullRequestAction(this, PullRequestAction.Approve);

    [RelayCommand]
    private void RequestChanges() => _owner.BeginPullRequestAction(this, PullRequestAction.RequestChanges);

    [RelayCommand]
    private void Comment() => _owner.BeginPullRequestAction(this, PullRequestAction.Comment);

    [RelayCommand]
    private void Merge() => _owner.BeginPullRequestAction(this, PullRequestAction.Merge);

    [RelayCommand]
    private void ClosePullRequest() => _owner.BeginPullRequestAction(this, PullRequestAction.Close);

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            Show(await services.PullRequestReviews.ReadAsync(_item.Repository, _item.Number));
        }
        catch (StewardException exception)
        {
            Error = _owner.StewardPullRequestErrorText(exception);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Show(PullRequestReview? pull)
    {
        PullRequest = pull;
        Files.Clear();
        Documents.Clear();
        foreach (var file in pull?.Files ?? [])
            Files.Add(new StewardReviewFile(_owner, this, file));

        foreach (var document in pull?.Documents ?? [])
            Documents.Add(new StewardReviewDocument(_owner, this, document, pull!.Author));

        OnPropertyChanged(string.Empty);
    }
}

/// <summary>One changed file of the review, with its diff when GitHub sends one.</summary>
public sealed partial class StewardReviewFile(MainViewModel owner, StewardReview review, PullRequestFile file)
{
    public PullRequestFile File { get; } = file;

    public string StatusText => File.Status switch
    {
        "added" => owner.Localization.StewardReviewFileAdded,
        "modified" => owner.Localization.StewardReviewFileModified,
        "removed" => owner.Localization.StewardReviewFileRemoved,
        "renamed" => owner.Localization.FormatStewardReviewFileRenamed(File.PreviousPath ?? string.Empty),
        _ => File.Status,
    };

    public string ChangesText => $"+{File.Additions} -{File.Deletions}";

    public bool HasPatch => File.Patch is not null;

    public IReadOnlyList<StewardPatchLine> Lines { get; } = file.Patch?.Split('\n').Select(line => new StewardPatchLine(line.TrimEnd('\r'))).ToList() ?? [];

    [RelayCommand]
    private void Open() => review.OpenLink(File.DiffUrl.AbsoluteUri);
}

/// <summary>One line of a unified diff.</summary>
public sealed record StewardPatchLine(string Text)
{
    public bool IsAdded => Text.StartsWith('+');

    public bool IsRemoved => Text.StartsWith('-');

    public bool IsHunk => Text.StartsWith("@@", StringComparison.Ordinal);
}

/// <summary>
/// A listing or pack document at the head commit, as the content page would show it, or its text and why it does not parse.
/// A listing also shows the ownership proof of the pull request author.
/// </summary>
public sealed partial class StewardReviewDocument
{
    private readonly MainViewModel _owner;
    private readonly StewardReview _review;
    private readonly ListingDraft? _draft;

    public StewardReviewDocument(MainViewModel owner, StewardReview review, PullRequestDocument document, string? author)
    {
        _owner = owner;
        _review = review;
        Document = document;
        Author = author;
        _draft = document.Document is { } table ? ListingDraft.FromDocument(table) : null;

        // the document is not validated yet, so only a link that opens a web page is offered
        Links = _draft is null ? [] : owner.ContentLinksOf(_draft.Links
            .Where(link => Uri.TryCreate(link.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
            .DistinctBy(link => link.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(link => link.Key, link => link.Url, StringComparer.OrdinalIgnoreCase));
        Members = document.Document is { } pack && document.Kind == StewardQueueKind.Pack
            ? new[] { "mods", "vehicles", "saves" }
                .SelectMany(key => pack.GetList(key)?.OfType<AuthoredTable>() ?? [])
                .Select(entry => $"{entry.GetString("id")} {entry.GetString("version")}".Trim())
                .ToList()
            : [];
        OwnershipLinks = document.Ownership is { } ownership
            ? new (string Label, Uri? Url, string Key)[]
            {
                (owner.Localization.ListingOpenRepository, ownership.RepositoryUrl, "repository"),
                (owner.Localization.ListingOpenSpaceDock, ownership.SpaceDockModUrl, "spacedock"),
                (owner.Localization.LinkForum, ownership.ForumsThread, "forums"),
            }.Where(link => link.Url is not null).Select(link => new ContentLink(link.Label, link.Url!.AbsoluteUri, link.Key)).ToList()
            : [];
    }

    public PullRequestDocument Document { get; }

    /// <summary>The login of the pull request author, whose proof shows.</summary>
    public string? Author { get; }

    public string Path => Document.Path;

    public string KindText => _owner.StewardQueueKindText(Document.Kind);

    public bool IsPreview => _draft is not null;

    /// <summary>Why the document shows as text, or null when it parses.</summary>
    public string? Problem => Document switch
    {
        { Text: null } => _owner.Localization.StewardReviewNotText,
        { ParseError: { } error } => _owner.Localization.FormatStewardReviewParseError(error),
        _ => null,
    };

    public string? RawText => _draft is null ? Document.Text : null;

    public string Name => _draft is { } draft ? draft.Name.Length > 0 ? draft.Name : draft.Id : string.Empty;

    public string? AuthorsText => _draft is { Authors.Count: > 0 } draft ? _owner.Localization.FormatContentByAuthor(string.Join(", ", draft.Authors)) : null;

    public string? Abstract => _draft?.Abstract is { Length: > 0 } text ? text : null;

    public string? Description => _draft?.Description is { Length: > 0 } text ? text : null;

    /// <summary>The tags as the content page shows them, so the preview reads like the page players will see.</summary>
    public IReadOnlyList<string> Tags => _draft is { } draft
        ? DiscoverItem.DisplayTags(_owner, Document.Kind == StewardQueueKind.Pack ? ContentType.ModPack : ContentType.Mod, draft.Tags)
        : [];

    public string? License => _draft?.License is { Length: > 0 } license ? license : null;

    public string? GameVersionText => _draft is { GameMin.Length: > 0 } draft
        ? draft.GameMax is null ? $">= {draft.GameMin}" : $"{draft.GameMin} - {draft.GameMax}"
        : null;

    public string? VersionText => Document.Kind == StewardQueueKind.Pack && Document.Document?.GetString("version") is { Length: > 0 } version
        ? _owner.Localization.FormatStewardVersion(version)
        : null;

    public IReadOnlyList<ContentLink> Links { get; }

    /// <summary>The pinned mods, vehicles and saves of a pack, each with its version.</summary>
    public IReadOnlyList<string> Members { get; }

    public bool HasOwnership => Document.Ownership is not null;

    public string? OwnershipText => (Document.Ownership, Author) switch
    {
        (null, _) => null,
        (_, null) => _owner.Localization.StewardReviewOwnershipNoAuthor,
        ({ State: ListingOwnershipState.Verified }, { } login) => _owner.Localization.FormatStewardReviewOwnershipVerified(login),
        ({ State: ListingOwnershipState.NotVerified }, { } login) => _owner.Localization.FormatStewardReviewOwnershipMissing(login),
        (_, { } login) => _owner.Localization.FormatStewardReviewOwnershipUnknown(login),
    };

    public bool IsOwnershipVerified => Document.Ownership?.State == ListingOwnershipState.Verified;

    /// <summary>The proof that the checks find, or the one that is missing, told about the author and not to the author.</summary>
    public string? OwnershipDetail
    {
        get
        {
            if (Document.Ownership is not { } ownership || Author is not { } login)
                return null;

            var repository = ownership.Repository ?? string.Empty;
            var detail = ownership.State == ListingOwnershipState.Verified
                ? ownership.Proof switch
                {
                    ListingOwnershipProof.Owner => _owner.Localization.FormatStewardReviewProofOwner(login, repository),
                    ListingOwnershipProof.Topic => _owner.Localization.FormatListingProofTopic(repository, ListingOwnership.TopicFor(login)),
                    ListingOwnershipProof.MarkerFile => _owner.Localization.FormatStewardReviewProofMarker(login, repository),
                    _ => null,
                }
                : ownership.Problem switch
                {
                    ListingOwnershipProblem.NoProof => _owner.Localization.FormatStewardReviewProofNone(login, repository, ListingOwnership.TopicFor(login)),
                    ListingOwnershipProblem.NoHost => _owner.Localization.StewardReviewProofNoHost,
                    ListingOwnershipProblem.RepositoryMissing => _owner.Localization.FormatListingFixMissing(repository),
                    ListingOwnershipProblem.RepositoryFork => _owner.Localization.FormatStewardReviewProofFork(login, repository, ListingOwnership.TopicFor(login)),
                    ListingOwnershipProblem.RepositoryRenamed => _owner.Localization.FormatStewardReviewProofRenamed(repository, ownership.RenamedTo ?? string.Empty),
                    ListingOwnershipProblem.SpaceDockModUnusable => _owner.Localization.FormatListingFixSpaceDockMod(ownership.SpaceDockMod ?? string.Empty),
                    ListingOwnershipProblem.SpaceDockNoSourceLink => _owner.Localization.FormatStewardReviewProofSpaceDockLink(ownership.SpaceDockMod ?? string.Empty),
                    _ => null,
                };

            return ownership.IsThroughSpaceDockLink && detail is not null
                ? _owner.Localization.FormatListingProofSpaceDockLink(ownership.SpaceDockMod!, repository) + " " + detail
                : detail;
        }
    }

    /// <summary>The repository, the SpaceDock mod and the forums thread that the proof is about.</summary>
    public IReadOnlyList<ContentLink> OwnershipLinks { get; }

    [RelayCommand]
    private void OpenLink(ContentLink link) => _review.OpenLink(link.Url);
}
