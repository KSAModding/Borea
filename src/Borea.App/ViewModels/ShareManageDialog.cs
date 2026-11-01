using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Sharing;
using Borea.Core.Tags;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>What the player does to their listing in the dialog.</summary>
internal enum ShareManageAction
{
    Change,
    TakeDown,
}

/// <summary>The text of a listing as Borea knows it, which the fields start from. A field Borea does not know is empty and goes out only when the player fills it.</summary>
internal sealed record ShareManageFields(string Name, string Abstract, string? Description, string License, IReadOnlyList<string> Tags)
{
    public ShareManageFields With(ShareListingChange change) =>
        new(change.Name ?? Name, change.Abstract ?? Abstract, change.Description ?? Description, change.License ?? License, change.Tags ?? Tags);
}

/// <summary>A listing that the record of a share says the player owns on a share host.</summary>
/// <param name="ListingId">The id of the listing on the host.</param>
/// <param name="Page">The page of the listing on the host, or null when Borea knows none.</param>
/// <param name="Versions">The versions that can be taken down, newest first.</param>
internal sealed record ShareManageTarget(ShareHost Host, ContentType Type, string ListingId, string Name, Uri? Page, ShareManageFields Fields, IReadOnlyList<string> Versions);

/// <summary>
/// Changes the text of a listing on its share host, or takes one of its versions down after the player confirmed that version. Both requests are optional for a
/// host, so the link to the page of the listing stays on the dialog, where the owner can do the same.
/// </summary>
public sealed partial class ShareManageDialog : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly ShareManageTarget _target;
    private readonly ShareManageAction _action;
    private CancellationTokenSource? _run;
    private (string Page, string? CompletePage, string Code)? _code;
    private Uri? _page;
    private bool _choseOtherLicense;

    internal ShareManageDialog(MainViewModel owner, ShareManageTarget target, ShareManageAction action)
    {
        _owner = owner;
        _target = target;
        _action = action;
        _page = target.Page;
        _name = target.Fields.Name;
        _abstract = target.Fields.Abstract;
        _description = target.Fields.Description ?? string.Empty;
        _license = target.Fields.License;
        _freeTags = string.Join(", ", target.Fields.Tags);
        _choseOtherLicense = _license.Length > 0 && !ListingEditor.IsListedLicense(_license);
        foreach (var version in target.Versions)
            Versions.Add(version);
        if (Versions.Count == 1)
            _selectedVersion = Versions[0];
        if (IsChange)
            RefreshChange();
    }

    public LocalizationService Localization => _owner.Localization;

    public string Title => IsChange ? Localization.FormatShareManageChangeTitle(_target.Name) : Localization.FormatShareTakeDownTitle(_target.Name);

    public bool IsChange => _action == ShareManageAction.Change;

    public bool ShowsChangeFields => IsChange && DoneText is null;

    public bool ShowsTakeDown => !IsChange && DoneText is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isSending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(CanTakeDown), nameof(ShowsVersionChoice))]
    [NotifyCanExecuteChangedFor(nameof(SendChangeCommand), nameof(TakeDownCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string? _error;

    /// <summary>The lines of the host for each refused field, under the message.</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>The account that is signed in does not own the listing, so another account is the way forward.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignOutText))]
    private bool _isRefusedOwner;

    public string? SignOutText => IsRefusedOwner ? Localization.FormatShareSignOut(_target.Host.Name) : null;

    /// <summary>What the host did, which ends the dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChangeFields), nameof(ShowsTakeDown), nameof(CanSend), nameof(CanTakeDown))]
    [NotifyCanExecuteChangedFor(nameof(SendChangeCommand), nameof(TakeDownCommand))]
    private string? _doneText;

    [ObservableProperty]
    private Uri? _pullRequest;

    /// <summary>The way to the same action on the site of the host, also while Borea can do it, because a host need not offer the requests.</summary>
    public string ManageLinkText => _page is null ? Localization.FormatShareOpenHost(_target.Host.Name) : Localization.FormatShareManageOnHost(_target.Host.Name);

    public Uri ManageLink => _page ?? _target.Host.Page;

    public bool CanClose => !IsSending;

    #region Change

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _abstract;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLicense))]
    private string _license;

    [ObservableProperty]
    private string _freeTags;

    [ObservableProperty]
    private string _sentenceCount = string.Empty;

    [ObservableProperty]
    private string? _sentenceError;

    [ObservableProperty]
    private string? _licenseError;

    [ObservableProperty]
    private string? _tagsError;

    /// <summary>Why Send is disabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendChangeCommand))]
    private string? _sendReason;

    public bool CanSend => SendReason is null && !IsRunning && DoneText is null;

    public IReadOnlyList<ShareLicenseChoice> LicenseChoices => ListingEditor.ShareLicenseChoicesIn(Localization);

    public bool IsOtherLicense => _choseOtherLicense;

    public ShareLicenseChoice? SelectedLicense
    {
        get => IsOtherLicense ? LicenseChoices[^1] : LicenseChoices.FirstOrDefault(choice => choice.Value == License.Trim());
        set
        {
            // The box clears its selection while its list changes with the language.
            if (value is null)
                return;

            _choseOtherLicense = value.Value is null;
            if (value.Value is not null)
                License = value.Value;
            else if (ListingEditor.IsListedLicense(License))
                License = string.Empty;

            OnPropertyChanged(nameof(IsOtherLicense));
            OnPropertyChanged(nameof(SelectedLicense));
        }
    }

    partial void OnNameChanged(string value) => RefreshChange();

    partial void OnAbstractChanged(string value) => RefreshChange();

    partial void OnDescriptionChanged(string value) => RefreshChange();

    partial void OnLicenseChanged(string value) => RefreshChange();

    partial void OnFreeTagsChanged(string value) => RefreshChange();

    private bool IsNameChanged => Name.Trim() != _target.Fields.Name;

    private bool IsAbstractChanged => Abstract.Trim() != _target.Fields.Abstract;

    private bool IsLicenseChanged => License.Trim() != _target.Fields.License;

    private string TypedDescription => Description.Replace("\r\n", "\n", StringComparison.Ordinal);

    private bool IsDescriptionChanged => TypedDescription != (_target.Fields.Description ?? string.Empty);

    private bool AreTagsChanged => !ReadTags(out _).SequenceEqual(_target.Fields.Tags, StringComparer.Ordinal);

    /// <summary>The checks of the describe step, for what the player changed. A field that stays does not go out, so it cannot stop the change.</summary>
    private void RefreshChange()
    {
        var length = Abstract.Trim().EnumerateRunes().Count();
        SentenceCount = Localization.FormatDiscoverCountShown(length, ListingDraft.AbstractLimit.ToString(CultureInfo.CurrentCulture));
        SentenceError = IsAbstractChanged && length > ListingDraft.AbstractLimit ? Localization.FormatShareSentenceTooLong(length, ListingDraft.AbstractLimit) : null;
        var tags = ReadTags(out var invalidTag);
        TagsError = invalidTag is null ? null : Localization.FormatListingTagInvalid(invalidTag);

        var issues = ChangedFieldIssues(tags);
        LicenseError = License.Trim().Length > 0 && issues.Contains("license") ? Localization.ShareLicenseInvalid : null;

        var missing = new[]
        {
            (Value: Name, Known: _target.Fields.Name, Label: Localization.ShareMissingName),
            (Value: Abstract, Known: _target.Fields.Abstract, Label: Localization.ShareMissingSentence),
            (Value: License, Known: _target.Fields.License, Label: Localization.ShareMissingLicense),
        }.Where(field => field.Known.Length > 0 && field.Value.Trim().Length == 0).Select(field => field.Label).ToList();

        var otherFields = issues.Where(location => location is not "license").Select(FieldLabel).Distinct().ToList();
        SendReason = missing.Count > 0 ? Localization.FormatListingStillMissing(string.Join(", ", missing))
            : SentenceError ?? LicenseError ?? TagsError
            ?? (otherFields.Count > 0 ? Localization.FormatShareFieldsToFix(string.Join(", ", otherFields))
            : IsNameChanged || IsAbstractChanged || IsDescriptionChanged || IsLicenseChanged || AreTagsChanged ? null
            : Localization.ShareManageNoChange);
    }

    /// <summary>The fields whose new value the checks of a listing refuse.</summary>
    private List<string> ChangedFieldIssues(IReadOnlyList<string> tags)
    {
        if (_owner.Services is not { } services)
            return [];

        var draft = new ListingDraft
        {
            Id = _target.ListingId,
            Type = TypeText,
            Name = Name.Trim(),
            Abstract = Abstract.Trim(),
            Description = Description.Length == 0 ? null : TypedDescription,
            License = License.Trim(),
            Tags = tags,
        };
        var changed = new List<string>();
        if (IsNameChanged)
            changed.Add("name");
        if (IsAbstractChanged)
            changed.Add("abstract");
        if (IsDescriptionChanged)
            changed.Add("description");
        if (IsLicenseChanged)
            changed.Add("license");
        if (AreTagsChanged)
            changed.Add("tags");

        return services.ListingValidator.Validate(draft.ToDocument(), new ListingCheckContext(null, _target.ListingId)).Issues
            .Where(issue => issue.Severity == ListingIssueSeverity.Error)
            .Select(issue => changed.FirstOrDefault(field => issue.Location == field || issue.Location.StartsWith(field + "[", StringComparison.Ordinal)))
            .OfType<string>()
            .Distinct()
            .ToList();
    }

    private string FieldLabel(string location) => location switch
    {
        "name" => Localization.ListingName,
        "abstract" => Localization.ShareOneSentence,
        "description" => Localization.ListingDescription,
        _ => Localization.ListingTags,
    };

    private List<string> ReadTags(out string? invalid)
    {
        invalid = null;
        var tags = new List<string>();
        foreach (var entry in ListingEditor.FreeTagEntries(FreeTags))
        {
            var tag = TagText.Normalize(entry);
            if (!TagText.IsValid(tag))
                invalid ??= entry;
            else if (!tags.Contains(tag, StringComparer.Ordinal))
                tags.Add(tag);
        }

        return tags;
    }

    private bool CanRunSendChange() => CanSend;

    [RelayCommand(CanExecute = nameof(CanRunSendChange))]
    private async Task SendChangeAsync()
    {
        var change = new ShareListingChange(
            IsNameChanged ? Name.Trim() : null,
            IsAbstractChanged ? Abstract.Trim() : null,
            IsDescriptionChanged ? TypedDescription : null,
            IsLicenseChanged ? License.Trim() : null,
            AreTagsChanged ? ReadTags(out _) : null);

        await RunAsync(Localization.FormatShareManageSending(_target.Host.Name), async (client, token) =>
        {
            var changed = await client.ChangeListingAsync(_target.Host, TypeText, _target.ListingId, change, token);
            if (changed.Page is { } page)
            {
                _page = page;
                RefreshLink();
            }

            _owner.RememberSentShareChange(_target, change);
            PullRequest = changed.PullRequest;
            DoneText = changed.PullRequest is null ? Localization.FormatShareManageSentLater(_target.Host.Name) : Localization.FormatShareManageSent(_target.Host.Name);
        });
    }

    #endregion

    #region TakeDown

    public ObservableCollection<string> Versions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TakeDownWarning), nameof(TakeDownText), nameof(CanTakeDown))]
    [NotifyCanExecuteChangedFor(nameof(TakeDownCommand))]
    private string? _selectedVersion;

    public bool ShowsVersionChoice => Versions.Count > 1 && !IsRunning;

    /// <summary>A listing can have no version that Borea knows, and the link on the dialog is then the only way.</summary>
    public bool HasNoVersion => Versions.Count == 0;

    /// <summary>The confirmation names one version, so nothing shows until the player chose one.</summary>
    public string? TakeDownWarning => SelectedVersion is { } version ? Localization.FormatShareTakeDownWarning(version, _target.Name, _target.Host.Name) : null;

    public string? TakeDownText => SelectedVersion is { } version ? Localization.FormatShareTakeDownVersion(version) : null;

    public bool CanTakeDown => SelectedVersion is not null && !IsRunning && DoneText is null;

    private bool CanRunTakeDown() => CanTakeDown;

    /// <summary>The one place that sends a take-down, because it cannot be undone. The version is the one that the button names.</summary>
    [RelayCommand(CanExecute = nameof(CanRunTakeDown))]
    private async Task TakeDownAsync()
    {
        if (SelectedVersion is not { } version)
            return;

        await RunAsync(Localization.FormatShareTakingDown(version), async (client, token) =>
        {
            await client.TakeDownReleaseAsync(_target.Host, TypeText, _target.ListingId, version, token);
            Versions.Remove(version);
            SelectedVersion = null;
            DoneText = Localization.FormatShareTakenDown(version);
            await _owner.ShowShareVersionTakenDownAsync(_target, version, Versions.ToList());
        }, version);
    }

    #endregion

    #region Sign-in and requests

    private string TypeText => _target.Type == ContentType.Save ? ListingFolders.SaveType : ListingFolders.VehicleType;

    public string? UserCode => _code?.Code;

    public string? CodeText => _code is not { } code ? null
        : code.CompletePage is null ? Localization.FormatListingShareHostCode(code.Page) : Localization.FormatShareSignIn(_target.Host.Name);

    public string? CodeWarningText => _code is null ? null : Localization.FormatShareCodeWarning(_target.Host.Name);

    public bool HasCodePage => _code?.CompletePage is not null;

    /// <summary>Signs in when needed, sends once more after a new sign-in when the host no longer takes the token, and shows a refusal. A host that also refuses the new token ends the sign-in.</summary>
    /// <param name="takenDownVersion">The version of a take-down, whose lost answer can mean that it is gone.</param>
    private async Task RunAsync(string progress, Func<IShareHostClient, CancellationToken, Task> send, string? takenDownVersion = null)
    {
        if (_owner.Services is not { } services)
            return;

        var client = services.ShareHostClient;
        using var cancel = new CancellationTokenSource();
        _run = cancel;
        IsRunning = true;
        Error = null;
        ErrorDetail = null;
        IsRefusedOwner = false;

        // Only a request that went out can have gone through, so a failed sign-in does not speak for the take-down.
        var isRequestOut = false;
        try
        {
            if (!client.IsSignedIn(_target.Host))
                await SignInAsync(client, progress, cancel);

            try
            {
                isRequestOut = true;
                await SendAsync(client, progress, send, cancel.Token);
            }
            catch (ShareHostException exception) when (exception.Failure == ShareHostFailure.SignedOut)
            {
                isRequestOut = false;
                await SignInAsync(client, progress, cancel);
                try
                {
                    isRequestOut = true;
                    await SendAsync(client, progress, send, cancel.Token);
                }
                catch (ShareHostException again) when (again.Failure == ShareHostFailure.SignedOut)
                {
                    client.SignOut(_target.Host);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (ShareHostException exception)
        {
            ShowFailure(exception, isRequestOut ? takenDownVersion : null);
        }
        finally
        {
            _run = null;
            ProgressText = null;
            IsRunning = false;
        }
    }

    /// <summary>Close stays off while a request is out, because leaving cannot take it back.</summary>
    private async Task SendAsync(IShareHostClient client, string progress, Func<IShareHostClient, CancellationToken, Task> send, CancellationToken token)
    {
        ProgressText = progress;
        IsSending = true;
        try
        {
            await send(client, token);
        }
        finally
        {
            IsSending = false;
        }
    }

    private async Task SignInAsync(IShareHostClient client, string progress, CancellationTokenSource cancel)
    {
        ProgressText = Localization.FormatListingShareHostSigningIn(_target.Host.Name);
        var codes = new Progress<GitHubDeviceCode>(code =>
        {
            if (_run != cancel)
                return;

            _code = (code.VerificationUri, code.VerificationUriComplete, code.UserCode);
            OpenCodePage();
            RefreshCode();
        });
        try
        {
            await client.SignInAsync(_target.Host, codes, _owner.StaySignedInToGitHub, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
        }
        finally
        {
            _code = null;
            RefreshCode();
            ProgressText = progress;
        }
    }

    private void ShowFailure(ShareHostException exception, string? takenDownVersion)
    {
        var host = _target.Host.Name;
        IsRefusedOwner = exception.Error == "not_owner";
        ErrorDetail = exception is { Error: "invalid_request", FieldErrors.Count: > 0 } ? string.Join('\n', exception.FieldErrors!.Values.SelectMany(lines => lines).Distinct()) : null;
        Error = exception switch
        {
            { Failure: ShareHostFailure.NotOffered } => Localization.FormatShareManageNotOffered(host),

            // A take-down without a clear answer can have gone through, and sending it again does not tell.
            { Failure: not (ShareHostFailure.Host or ShareHostFailure.SignedOut or ShareHostFailure.SignInRefused) } or { Failure: ShareHostFailure.Host, IsServerError: true } when takenDownVersion is { } version =>
                Localization.FormatShareTakeDownLostAnswer(host, version),
            _ when ListingEditor.AnyRequestFailureText(Localization, host, exception) is { } text => text,
            { Error: "not_owner" } => exception.Message,
            _ => Localization.FormatListingShareHostSaid(host, exception.Message),
        };
    }

    private void RefreshCode()
    {
        OnPropertyChanged(nameof(UserCode));
        OnPropertyChanged(nameof(CodeText));
        OnPropertyChanged(nameof(CodeWarningText));
        OnPropertyChanged(nameof(HasCodePage));
    }

    private void RefreshLink()
    {
        OnPropertyChanged(nameof(ManageLink));
        OnPropertyChanged(nameof(ManageLinkText));
    }

    [RelayCommand]
    private void OpenCodePage()
    {
        if (_code is not { CompletePage: { } page } code || OpenPage(page))
            return;

        // A system that opens no browser leaves the address to type, which the dialog names only when no page has the code filled in.
        _code = code with { CompletePage = null };
        RefreshCode();
    }

    [RelayCommand]
    private async Task CopyCodeAsync()
    {
        if (_code is { } code && _owner.WindowServices is { } window)
            await window.CopyTextAsync(code.Code);
    }

    [RelayCommand]
    private async Task CopyCodeAndOpenAsync()
    {
        if (_code is not { } code)
            return;

        await CopyCodeAsync();
        OpenPage(code.Page);
    }

    private bool OpenPage(string url)
    {
        if (_owner.OpenListingPage(url) is not { } problem)
            return true;

        ErrorDetail = Localization.FormatListingShareHostOpenFailed(problem);
        return false;
    }

    [RelayCommand]
    private void OpenManageLink() => OpenPage(ManageLink.AbsoluteUri);

    [RelayCommand]
    private void OpenPullRequest()
    {
        if (PullRequest is { } request)
            OpenPage(request.AbsoluteUri);
    }

    /// <summary>The next try signs in again, with the account that owns the listing.</summary>
    [RelayCommand]
    private void SignOut()
    {
        _owner.Services?.ShareHostClient.SignOut(_target.Host);
        IsRefusedOwner = false;
        Error = null;
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        _run?.Cancel();
        _owner.CloseShareManage(this);
    }

    #endregion
}
