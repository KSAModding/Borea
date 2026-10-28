using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// A mod author's request to take their mod out of a pack, from its takedown report (RFC 0080). It reads the mods that the pack pins,
/// takes the one that the report names, and checks that the reporter owns its listing by the ownership proof. Without the proof the
/// steward confirms it by the forums account. Then it opens one pull request that retracts every version of the pack that pins the mod.
/// </summary>
public sealed partial class PackLeaveDialog : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly IndexReport _report;
    private Task _run = Task.CompletedTask;
    private int _checks;
    private IndexStatusRefusal? _refused;

    /// <param name="report">A takedown that names the pack in its listing id field.</param>
    public PackLeaveDialog(MainViewModel owner, IndexReport report)
    {
        _owner = owner;
        _report = report;
        PackId = report.Id ?? string.Empty;
    }

    public LocalizationService Localization => _owner.Localization;

    public string PackId { get; }

    public string Title => ModId is { } mod ? Localization.FormatStewardLeaveTitle(PackId, mod) : Localization.FormatStewardLeavePackTitle(PackId);

    /// <summary>The mods that the versions of the pack pin on the base branch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMembers))]
    [NotifyPropertyChangedFor(nameof(MembersText))]
    private IReadOnlyList<string> _members = [];

    /// <summary>The mod to take out, which the report names or the steward chooses.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Reason))]
    [NotifyPropertyChangedFor(nameof(ConfirmText))]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private string? _modId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(MembersText))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(VersionsText))]
    [NotifyPropertyChangedFor(nameof(RetractedText))]
    [NotifyPropertyChangedFor(nameof(OwnershipText))]
    [NotifyPropertyChangedFor(nameof(OwnershipDetail))]
    [NotifyPropertyChangedFor(nameof(IsProven))]
    [NotifyPropertyChangedFor(nameof(NeedsConfirmation))]
    [NotifyPropertyChangedFor(nameof(CanOpenForums))]
    [NotifyPropertyChangedFor(nameof(OpenPullRequestsText))]
    [NotifyPropertyChangedFor(nameof(OwnerText))]
    [NotifyPropertyChangedFor(nameof(MentionText))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private PackLeaveCheck? _check;

    /// <summary>Why Borea would not open the pull request.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private string? _refusal;

    /// <summary>The steward confirmed by the forums account of the listing that the reporter owns the mod.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private bool _confirmedByForums;

    /// <summary>While the pull request is being opened, the dialog cannot close, so the steward sees how it ended.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isOpening;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(MembersText))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(IsOpened))]
    [NotifyPropertyChangedFor(nameof(OpenedText))]
    [NotifyPropertyChangedFor(nameof(NeedsConfirmation))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private IndexStatusPullRequest? _opened;

    public bool HasMembers => Members.Count > 0;

    /// <summary>
    /// Asks the steward to choose the mod, or says that the id is no pack, because every accepted pack pins a mod. A read that failed
    /// says nothing about the pack.
    /// </summary>
    public string? MembersText => (IsChecking || Error is not null) && Members.Count == 0 ? null
        : Members.Count == 0 ? Localization.FormatStewardRefusedNotAPack(PackId)
        : Localization.StewardLeaveChooseMod;

    /// <summary>The mod and the pack can change until the pull request is open.</summary>
    public bool CanEdit => !IsOpening && Opened is null;

    public string Reason => Leave()?.Reason ?? string.Empty;

    public string ReportText => Localization.FormatStewardReportCloses(_report.Number.ToString(CultureInfo.InvariantCulture));

    public string? ReporterText => _owner.IsReporter(_report) ? Localization.StewardReporterWarning : null;

    public string? VersionsText => Check is { Versions.Count: > 0 } check ? Localization.FormatStewardLeaveVersions(string.Join(", ", check.Versions)) : null;

    public string? RetractedText => Check is { Retracted.Count: > 0 } check ? Localization.FormatStewardLeaveRetracted(string.Join(", ", check.Retracted)) : null;

    public bool IsProven => Check?.IsProven == true;

    /// <summary>Whether the ownership proof names the reporter as an owner of the listing of the mod, or could not be checked.</summary>
    public string? OwnershipText => (Check, ModId) switch
    {
        (null, _) or (_, null) => null,
        ({ Ownership.State: ListingOwnershipState.Verified }, { } mod) => Localization.FormatStewardLeaveReporterOwns(Reporter, mod),
        ({ Ownership.State: ListingOwnershipState.NotVerified }, { } mod) => Localization.FormatStewardLeaveReporterUnproven(Reporter, mod),
        _ => Localization.FormatStewardReviewOwnershipUnknown(Reporter),
    };

    /// <summary>The proof that names the reporter, or the one that is missing, as the review of a listing shows it.</summary>
    public string? OwnershipDetail => Check is { Ownership: var ownership } && ModId is not null ? _owner.OwnershipProofText(ownership, Reporter) : null;

    /// <summary>The proof does not name the reporter, so the steward confirms it by the forums account before the pull request opens.</summary>
    public bool NeedsConfirmation => Check is { IsProven: false, Refusal: null } && Opened is null;

    public string? ConfirmText => ModId is { } mod ? Localization.FormatStewardLeaveConfirm(Reporter, mod) : null;

    public bool CanOpenForums => Check?.Ownership.ForumsThread is not null;

    public string? OpenPullRequestsText => Check switch
    {
        null => null,
        { OpenPullRequests: null } => Localization.StewardOpenPullRequestsUnknown,
        { OpenPullRequests.Count: 0 } => null,
        { OpenPullRequests: { } open } => Localization.FormatStewardOpenPullRequests(string.Join(", ", open.Select(pull => "#" + pull.Number.ToString(CultureInfo.InvariantCulture)))),
    };

    /// <summary>A steward who owns the pack is a party to the request, and POLICY.md asks a party to leave the case to another steward.</summary>
    public string? OwnerText => Check?.IsOwner == true ? Localization.StewardOwnerWarning : null;

    public string? MentionText => Check is { Owners.Count: > 0 } check
        ? Localization.FormatStewardMention(string.Join(", ", check.Owners.Select(login => "@" + login)))
        : null;

    public bool CanOpen => Check is { Refusal: null } check && Refusal is null && ModId is not null && !IsChecking && !IsOpening && Opened is null
        && (check.IsProven || ConfirmedByForums);

    public bool CanClose => !IsOpening;

    /// <summary>A read that failed can run again. After a failed pull request, the button that opens it stays.</summary>
    public bool CanRetry => !IsChecking && !IsOpening && Opened is null && Error is not null && Check is null;

    public bool IsOpened => Opened is not null;

    public string? OpenedText => Opened is { } pull ? Localization.FormatStewardOpened(pull.Number.ToString(CultureInfo.InvariantCulture)) : null;

    /// <summary>The login that filed the report.</summary>
    private string Reporter => _report.Author ?? string.Empty;

    internal Task WhenDoneAsync() => _run;

    /// <summary>Reads the mods of the pack, and checks the one the report names when it names exactly one.</summary>
    internal void Start() => _run = LoadAsync();

    /// <summary>The request with the mod that is chosen now, or null before a mod is chosen.</summary>
    internal PackLeave? Leave() => ModId is { } mod
        ? new PackLeave(PackId, mod, _report.Number)
        {
            Reporter = _report is { Author: { } login, AuthorId: { } id } ? new GitHubAccount(login, id) : null,
            ConfirmedByForums = ConfirmedByForums,
        }
        : null;

    /// <summary>Another mod needs its own check, and a confirmation by the forums account was about the mod before.</summary>
    partial void OnModIdChanged(string? value)
    {
        ConfirmedByForums = false;
        Check = null;
        SetRefusal(null);
        if (value is not null && Opened is null)
            _run = CheckAsync();
    }

    private void SetRefusal(IndexStatusRefusal? refusal)
    {
        _refused = refusal;
        Refusal = refusal switch
        {
            null => null,
            IndexStatusRefusal.NotPinned => Localization.FormatStewardRefusedNotPinned(PackId, ModId ?? string.Empty),
            IndexStatusRefusal.NotConfirmed => Localization.FormatStewardRefusedNotConfirmed(Reporter, ModId ?? string.Empty),
            { } other => _owner.IndexStatusRefusalText(other, PackId, null),
        };
    }

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsChecking = true;
        Error = null;
        try
        {
            Members = await services.IndexStatusEditor.PackMembersAsync(PackId);
        }
        catch (StewardException exception)
        {
            Error = _owner.StewardErrorText(exception);
            return;
        }
        finally
        {
            IsChecking = false;
        }

        if (_report.MentionedIds(Members) is [var named])
        {
            // the choice starts the check of the mod
            ModId = named;
            await _run;
        }
    }

    private async Task CheckAsync()
    {
        if (_owner.Services is not { } services || Leave() is not { } leave)
            return;

        var check = ++_checks;
        IsChecking = true;
        Error = null;
        try
        {
            var result = await services.IndexStatusEditor.CheckLeaveAsync(leave);
            if (check != _checks)
                return;

            SetRefusal(result.Refusal);
            Check = result;
        }
        catch (StewardException exception)
        {
            if (check == _checks)
                Error = _owner.StewardErrorText(exception);
        }
        finally
        {
            if (check == _checks)
                IsChecking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenPullRequestAsync()
    {
        // a second click while the first one runs sends nothing
        if (!CanOpen)
            return _run;

        return _run = OpenAsync();
    }

    private async Task OpenAsync()
    {
        if (_owner.Services is not { } services || Leave() is not { } leave)
            return;

        IsOpening = true;
        Error = null;
        try
        {
            Opened = await services.IndexStatusEditor.OpenLeaveAsync(leave);
            _owner.OnIndexStatusPullRequestOpened();
        }
        catch (IndexStatusRefusedException exception)
        {
            // the proof that the check found did not hold when the pull request was to open, so the steward confirms it by the forums account
            if (exception.Refusal == IndexStatusRefusal.NotConfirmed && Check is { IsProven: true } check)
                Check = check with { Ownership = ListingOwnership.Unknown with { ForumsThread = check.Ownership.ForumsThread } };

            SetRefusal(exception.Refusal);
        }
        catch (StewardException exception)
        {
            Error = _owner.StewardErrorText(exception);
        }
        finally
        {
            IsOpening = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync()
    {
        if (!CanRetry)
            return _run;

        return _run = Members.Count == 0 ? LoadAsync() : CheckAsync();
    }

    /// <summary>A confirmation that the steward gives after a refusal for its lack takes the refusal away.</summary>
    partial void OnConfirmedByForumsChanged(bool value)
    {
        if (value && _refused == IndexStatusRefusal.NotConfirmed)
            SetRefusal(null);
    }

    [RelayCommand]
    private void OpenForums()
    {
        if (Check?.Ownership.ForumsThread is { } thread)
            Error = _owner.TryOpenWithSystem(thread.AbsoluteUri) ?? Error;
    }

    [RelayCommand]
    private void ShowPullRequest()
    {
        if (Opened is { } pull)
            Error = _owner.TryOpenWithSystem(pull.Url.AbsoluteUri);
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        if (CanClose)
            _owner.ClosePackLeave(this);
    }
}
