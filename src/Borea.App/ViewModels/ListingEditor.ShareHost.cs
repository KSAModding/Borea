using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.GitHub;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Sharing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>A share host that takes the type of the shared item, as a row of the publish step.</summary>
/// <param name="LimitText">The largest file of the type, or null when the host names no limit.</param>
/// <param name="RulesText">The link to the rules of the host, or null when it names none.</param>
public sealed record ShareHostRow(ShareHost Host, string? LimitText, string? RulesText);

/// <summary>A release status that the host takes, with the name the App shows for it.</summary>
public sealed record ShareStatusChoice(string Value, string Label);

/// <summary>
/// A share on a host of RFC 0094: Borea writes the archive, signs in, uploads it with the dependencies, shows what the host
/// checked, and publishes the version and status the player chose. The host opens the listing pull request.
/// </summary>
public sealed partial class ListingEditor
{
    private IReadOnlyList<ShareHost> _shareHosts = [];
    private bool _hasReadShareHosts;
    private CancellationTokenSource? _shareHostRun;
    private ShareHost? _checkedHost;
    private IReadOnlyList<ShareCheck> _shareChecks = [];
    private (ShareHost Host, ShareUpload Upload, string? ListingId)? _shareUpload;
    private (string Name, ShareHost Host, Uri? Page, bool IsNewVersion)? _sharePublished;
    private (string HostName, string Page, string? CompletePage, string Code)? _shareHostCode;
    private CancellationTokenSource? _publishRun;
    private CancellationTokenSource? _signInRun;
    private ShareErrorAction _shareErrorAction;

    /// <summary>The one way forward that an error of a host offers.</summary>
    private enum ShareErrorAction
    {
        None,
        BackToDescription,
        SignOut,
        OpenHost,
    }

    /// <summary>The sections whose values a share host does not take.</summary>
    private static readonly string[] IndexOnlySections = ["links", "releases", "compatibility", "images"];

    /// <summary>The last reading of hosts.toml, which tests wait for.</summary>
    internal Task ShareHostsLoad { get; private set; } = Task.CompletedTask;

    /// <summary>The new check after a rename, which tests wait for. It is not part of the command of the modal, whose button would stay disabled during the upload.</summary>
    internal Task ShareCheckAfterRename { get; private set; } = Task.CompletedTask;

    public ObservableCollection<ShareHostRow> ShareHostRows { get; } = [];

    public bool HasShareHosts => HasShare && ShareHostRows.Count > 0;

    public bool HasSeveralShareHosts => ShareHostRows.Count > 1;

    /// <summary>Why no host can be chosen. One text is true for both cases, because the source does not tell no host from a list that cannot be read.</summary>
    public string? ShareNoHostText => HasShare && _hasReadShareHosts && ShareHostRows.Count == 0
        ? IsSave ? Localization.ShareNoHostSave : Localization.ShareNoHostVehicle
        : null;

    public string? ShareHostHeadingText => HasSeveralShareHosts
        ? Localization.ShareHostChoiceHeading
        : SelectedShareHostRow is { } row ? Localization.FormatShareHostOne(row.Host.Name) : null;

    public string? ShareCheckOnHostText => SelectedShareHostRow is { } row ? Localization.FormatShareCheckOnHost(row.Host.Name) : null;

    public string? ShareCheckHintText => SelectedShareHostRow is { } row ? Localization.FormatShareCheckHint(row.Host.Name) : null;

    public bool ShowsShareCheckButton => HasShareHosts && !HasShareUpload && !IsSharing;

    public ObservableCollection<string> ShareHostFailedChecks { get; } = [];

    public ObservableCollection<string> ShareHostPassedChecks { get; } = [];

    public string? ShareChecksPassedText => _shareUpload is { Upload.Checks.Count: > 0 } shared ? Localization.FormatShareChecksPassed(shared.Upload.Checks.Count, shared.Host.Name) : null;

    public bool HasShareDetails => ShareHostPassedChecks.Count > 0 || ShareHostListingText is not null || ShareHostFileDetail is not null;

    public string? ShareDetailsListingText => _checkedHost is { } host ? Localization.FormatShareDetailsListing(host.Name) : null;

    /// <summary>A failed check of the name or the folder, which a new name can fix. Any other check shows the message of the host alone, because check names may grow.</summary>
    public bool ShowsShareRename => _shareChecks.Any(check => check is { Passed: false, Name: "name" or "folder" });

    public string? ShareFolderCheckText => _shareChecks.Any(check => check is { Passed: false, Name: "folder" })
        ? IsSave ? Localization.ShareFolderCheckSave : Localization.ShareFolderCheckVehicle
        : null;

    public IReadOnlyList<ShareStatusChoice> ShareStatusChoices =>
        [new("stable", Localization.ReleaseStable), new("testing", Localization.ReleaseTesting), new("dev", Localization.ReleaseDev)];

    public ShareStatusChoice? SelectedShareStatus
    {
        get => ShareStatusChoices.FirstOrDefault(choice => choice.Value == ShareHostStatus);
        set
        {
            if (value is not null)
                ShareHostStatus = value.Value;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareHostHeadingText), nameof(ShareCheckOnHostText), nameof(ShareCheckHintText), nameof(ShareSignedInText), nameof(ShareSignOutText))]
    private ShareHostRow? _selectedShareHostRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublishToShareHost), nameof(CanUploadToShareHost), nameof(IsSharing), nameof(ShowsShareCheckButton), nameof(ShareHostProgressText), nameof(IsSendingSharePublish), nameof(CanCancelShareHost), nameof(ShareSignedInText), nameof(ShareSignOutText))]
    private string? _shareHostProgress;

    [ObservableProperty]
    private string? _shareHostError;

    /// <summary>What the host itself said, under the text of the App, because that text is in the language of the player and the message is not.</summary>
    [ObservableProperty]
    private string? _shareHostErrorDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShareDetails))]
    private string? _shareHostFileDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShareDetails))]
    private string? _shareHostListingText;

    [ObservableProperty]
    private bool _isShareDetailsOpen;

    [ObservableProperty]
    private bool _isEditingShareVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublishToShareHost), nameof(ShareVersionError), nameof(ShareVersionLineText), nameof(SharePublishText))]
    private string _shareHostVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedShareStatus))]
    private string _shareHostStatus = "stable";

    [ObservableProperty]
    private Uri? _sharePullRequest;

    /// <summary>The line beside the progress bar. The code comes from the progress of the sign-in, which can run after the share ended, so it must not write <see cref="ShareHostProgress"/>.</summary>
    public string? ShareHostProgressText => ShareHostUserCode is not null ? Localization.ShareWaitingForApproval : ShareHostProgress;

    public string? ShareHostCodeText => _shareHostCode is not { } code ? null
        : code.CompletePage is null ? Localization.FormatListingShareHostCode(code.Page) : Localization.FormatShareSignIn(code.HostName);

    public string? ShareHostUserCode => _shareHostCode?.Code;

    public string? ShareCodeWarningText => _shareHostCode is { } code ? Localization.FormatShareCodeWarning(code.HostName) : null;

    /// <summary>The host names a page with the code filled in, so the player only compares the code.</summary>
    public bool HasShareCodePage => _shareHostCode?.CompletePage is not null;

    /// <summary>An upload that the host can publish. One with a failed check is not kept, so the player shares again after a fix.</summary>
    public bool HasShareUpload => _shareUpload is not null;

    /// <summary>A share on a host runs.</summary>
    public bool IsSharing => ShareHostProgress is not null;

    public bool CanUploadToShareHost => !IsSharing;

    /// <summary>A publish is out at the host, and leaving cannot take it back. A sign-in during the publish can still be left, because the host refused what was sent before it.</summary>
    public bool IsSendingSharePublish => IsSharing && _shareHostRun == _publishRun && _shareHostRun != _signInRun;

    public bool CanCancelShareHost => IsSharing && !IsSendingSharePublish;

    public string? ShareErrorActionText => _shareErrorAction switch
    {
        ShareErrorAction.BackToDescription => Localization.ShareBackToDescription,
        ShareErrorAction.SignOut when _checkedHost is { } host => Localization.FormatShareSignOut(host.Name),
        ShareErrorAction.OpenHost when _checkedHost is { } host => Localization.FormatShareOpenHost(host.Name),
        _ => null,
    };

    /// <summary>The host that the line about the account names. An error that offers the sign-out itself takes its place, so the button shows once.</summary>
    private ShareHost? SignedInShareHost => _shareErrorAction != ShareErrorAction.SignOut && SelectedShareHostRow?.Host is { } host && _owner.Services is { } services && services.ShareHostClient.IsSignedIn(host) ? host : null;

    public string? ShareSignedInText => SignedInShareHost is { } host ? Localization.FormatShareSignedIn(host.Name) : null;

    public string? ShareSignOutText => SignedInShareHost is { } host ? Localization.FormatShareSignOut(host.Name) : null;

    public bool CanPublishToShareHost => _shareUpload is not null && IsShareVersionValid && !IsSharing;

    private bool IsShareVersionValid => ModVersion.TryParse(ShareHostVersion.Trim(), out _);

    public string? ShareVersionError => IsShareVersionValid ? null : Localization.ShareVersionInvalid;

    public string ShareVersionLineText => Localization.FormatShareVersionLine(ShareHostVersion.Trim());

    public string SharePublishText => IsShareVersionValid ? Localization.FormatSharePublishVersion(ShareHostVersion.Trim()) : Localization.ListingShareHostPublish;

    public string? SharePublicNoticeText => _shareUpload is { } shared ? Localization.FormatSharePublicNotice(shared.Host.Name) : null;

    public ShareHost? ShareUploadHost => _shareUpload?.Host;

    public string? ShareUploadRulesText => ShareUploadHost is { Policy: not null } host ? Localization.FormatShareHostRules(host.Name) : null;

    private void StartShareHosts()
    {
        ForgetShareHostRun();
        if (_owner.Services is { } services)
            ShareHostsLoad = LoadShareHostsAsync(services.ShareHosts, _share);
    }

    private async Task LoadShareHostsAsync(IShareHostSource source, GameSaveShareStart? share)
    {
        var hosts = await source.GetAsync();
        if (_share != share)
            return;

        _shareHosts = hosts;
        _hasReadShareHosts = true;
        RefreshShareHostRows();
        OnPropertyChanged(nameof(ShareHostText));
        OnPropertyChanged(nameof(ShareNewVersionText));
    }

    /// <summary>Reads the index and the hosts again. A share that started without the index also finds its listing that way.</summary>
    [RelayCommand]
    private async Task RetryShareHostsAsync()
    {
        await OpenAsync();
        StartShareHosts();
        await ShareHostsLoad;
    }

    private void RefreshShareHostRows()
    {
        // The list clears its selection while its rows change, so the chosen host is read first.
        var chosen = SelectedShareHostRow?.Host.Key;

        // A new version belongs on the host of its listing, because another host has no listing that it could add to.
        var rows = _share is null ? [] : _shareHosts
            .Where(host => host.Accepts(Type) && (_shareListing is not { } listed || listed.Entry.Host == host.Key))
            .Select(host => new ShareHostRow(host, ShareHostLimitText(host), host.Policy is null ? null : Localization.FormatShareHostRules(host.Name)))
            .ToList();
        MainViewModel.Arrange(ShareHostRows, rows);
        SelectedShareHostRow = rows.FirstOrDefault(row => row.Host.Key == chosen) ?? rows.FirstOrDefault();
        ShowShareChecks(_shareChecks);
        OnPropertyChanged(nameof(HasShareHosts));
        OnPropertyChanged(nameof(HasSeveralShareHosts));
        OnPropertyChanged(nameof(ShareNoHostText));
        OnPropertyChanged(nameof(ShareHostHeadingText));
        RefreshShareRun();
    }

    private string? ShareHostLimitText(ShareHost host)
    {
        if (host.MaxBytesFor(Type) is not { } max)
            return null;

        return IsSave ? Localization.FormatShareHostLimitSave(ShareHostSizeText(max)) : Localization.FormatShareHostLimitVehicle(ShareHostSizeText(max));
    }

    /// <summary>A limit of whole MiB reads as that number of MB, as the page of a host says it.</summary>
    private static string ShareHostSizeText(long bytes)
    {
        const long mebibyte = 1024 * 1024;
        return bytes % mebibyte == 0 ? $"{bytes / mebibyte} MB" : MainViewModel.SizeText(bytes);
    }

    /// <summary>Raises the values that follow the upload, the code and the language.</summary>
    private void RefreshShareRun()
    {
        OnPropertyChanged(nameof(HasShareUpload));
        OnPropertyChanged(nameof(CanPublishToShareHost));
        OnPropertyChanged(nameof(ShowsShareCheckButton));
        OnPropertyChanged(nameof(ShareChecksPassedText));
        OnPropertyChanged(nameof(ShareDetailsListingText));
        OnPropertyChanged(nameof(ShareVersionError));
        OnPropertyChanged(nameof(ShareVersionLineText));
        OnPropertyChanged(nameof(SharePublishText));
        OnPropertyChanged(nameof(SharePublicNoticeText));
        OnPropertyChanged(nameof(ShareUploadHost));
        OnPropertyChanged(nameof(ShareUploadRulesText));
        OnPropertyChanged(nameof(ShareDoneHeadingText));
        OnPropertyChanged(nameof(ShareDoneDiscoverText));
        OnPropertyChanged(nameof(ShareDoneLink));
        OnPropertyChanged(nameof(ShareDoneOpenText));
        RefreshShareErrorAction();
        RefreshShareHostCode();
    }

    private void RefreshShareHostCode()
    {
        OnPropertyChanged(nameof(ShareHostCodeText));
        OnPropertyChanged(nameof(ShareHostUserCode));
        OnPropertyChanged(nameof(ShareCodeWarningText));
        OnPropertyChanged(nameof(HasShareCodePage));
        OnPropertyChanged(nameof(ShareHostProgressText));
    }

    private void RefreshShareErrorAction()
    {
        OnPropertyChanged(nameof(ShareErrorActionText));
        OnPropertyChanged(nameof(ShareSignedInText));
        OnPropertyChanged(nameof(ShareSignOutText));
    }

    /// <summary>A text from any source starts with no detail and no action, and <see cref="ShowShareHostFailureAsync"/> sets them after it.</summary>
    partial void OnShareHostErrorChanged(string? value)
    {
        ShareHostErrorDetail = null;
        _shareErrorAction = ShareErrorAction.None;
        RefreshShareErrorAction();
    }

    /// <summary>Drops a running share and its result, because they belong to the item before.</summary>
    private void ForgetShareHostRun()
    {
        _shareHostRun?.Cancel();
        _shareHostRun = null;
        _shareUpload = null;
        _sharePublished = null;
        _shareHostCode = null;
        ShareHostProgress = null;
        ShareHostError = null;
        ShareHostFileDetail = null;
        ShareHostListingText = null;
        SharePullRequest = null;
        IsShareDetailsOpen = false;
        IsEditingShareVersion = false;
        ShowShareChecks([]);
        RefreshShareRun();
    }

    /// <summary>A new version sends only the mods of the draft. A new listing also has to pass in the sections that the host does not take, because the publish checks the whole draft.</summary>
    private bool StopsTheUpload(ListingIssue issue)
    {
        var section = SectionOf(issue);
        return section == "dependencies" || (!IsNewVersionShare && IndexOnlySections.Contains(section));
    }

    /// <summary>
    /// Writes the archive and refuses one above the limit of the host before any request. Then it signs in when needed and
    /// uploads, and shows what the host checked with the version it proposes.
    /// </summary>
    [RelayCommand]
    private async Task UploadToShareHostAsync(ShareHostRow? row)
    {
        if (row is null || _share is not { } share || _owner.Services is not { } services || !CanUploadToShareHost)
            return;

        var host = row.Host;
        ForgetShareHostRun();
        _checkedHost = host;
        if (Errors.Any(StopsTheUpload))
        {
            ShareHostError = Localization.ShareOtherFields;
            return;
        }

        using var cancel = new CancellationTokenSource();
        _shareHostRun = cancel;
        ShareHostProgress = Localization.ListingShareHostWriting;
        string? file = null;
        try
        {
            if (await _owner.WriteShareArchiveToTempAsync(share) is not { } written)
                return;

            file = written.File;
            if (_shareHostRun != cancel)
                return;

            if (host.MaxBytesFor(Type) is { } max && written.Archive.Size > max)
            {
                ShareHostError = Localization.FormatListingShareHostTooLarge(MainViewModel.SizeText(written.Archive.Size), host.Name, ShareHostSizeText(max));
                return;
            }

            var client = services.ShareHostClient;
            if (!client.IsSignedIn(host))
                await SignInToShareHostAsync(host, client, cancel);

            var listingId = _shareListing?.Entry.Reference;
            var upload = await SendSignedInAsync(host, client, cancel, Localization.FormatListingShareHostUploading(host.Name), async () =>
            {
                await using var archive = File.OpenRead(file);
                return await client.UploadAsync(host, archive, share.Content.Name + ".zip", Draft.Dependencies, listingId, cancel.Token);
            });

            if (_shareHostRun != cancel)
                return;

            ShowShareChecks(upload.Checks);
            ShareHostListingText = upload.ListingText;
            if (upload.Checks.All(check => check.Passed))
            {
                _shareUpload = (host, upload, listingId);
                ShareHostVersion = upload.ProposedVersion ?? string.Empty;

                // A proposal that cannot be published shows the field at once, because the line alone would not say what stops Publish.
                // A channel the player chose before this check shows it too, because the line does not name the channel.
                IsEditingShareVersion = !IsShareVersionValid || ShareHostStatus != "stable";
            }
            else
            {
                ShareHostError = Localization.FormatListingShareHostChecksFailed(host.Name);
            }

            RefreshShareRun();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (ShareHostException exception) when (_shareHostRun == cancel)
        {
            await ShowShareHostFailureAsync(host, share, exception, version: null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A run that was cancelled has no step left to show it on, and the exception must not end the App.
            if (_shareHostRun == cancel)
            {
                ShareHostError = Localization.FormatShareErrorFile(share.Content.Name);
                ShareHostFileDetail = exception.Message;
            }
        }
        finally
        {
            if (file is not null)
                MainViewModel.TryDeleteFile(file);
            if (_shareHostRun == cancel)
            {
                _shareHostRun = null;
                ShareHostProgress = null;
            }
        }
    }

    /// <summary>Signs in and shows the code until the sign-in ends. A host that names a page with the code filled in gets that page opened, so the player does not type the code.</summary>
    private async Task SignInToShareHostAsync(ShareHost host, IShareHostClient client, CancellationTokenSource cancel)
    {
        _signInRun = cancel;
        ShareHostProgress = Localization.FormatListingShareHostSigningIn(host.Name);
        var progress = new Progress<GitHubDeviceCode>(code =>
        {
            if (_shareHostRun != cancel)
                return;

            _shareHostCode = (host.Name, code.VerificationUri, code.VerificationUriComplete, code.UserCode);
            OpenShareHostCodePage();
            RefreshShareHostCode();
        });
        try
        {
            await client.SignInAsync(host, progress, _owner.StaySignedInToGitHub, cancel.Token);

            // A Cancel that lands before this line has cleared the progress, and the caller must not set it again.
            cancel.Token.ThrowIfCancellationRequested();
        }
        finally
        {
            if (_shareHostRun == cancel)
            {
                _signInRun = null;
                _shareHostCode = null;

                // An error that the page could not be opened belongs to the code that is gone.
                ShareHostError = null;
                RefreshShareHostCode();
            }
        }
    }

    /// <summary>
    /// Sends a request, and once more after a new sign-in when the host no longer takes the token, which the client has
    /// dropped by then. <paramref name="send"/> opens its own stream, because a request disposes the stream it sent. A host
    /// that also refuses the new token ends the sign-in, so the next share asks for a code and not for the same refresh.
    /// </summary>
    private async Task<T> SendSignedInAsync<T>(ShareHost host, IShareHostClient client, CancellationTokenSource cancel, string progress, Func<Task<T>> send)
    {
        ShareHostProgress = progress;
        try
        {
            return await send();
        }
        catch (ShareHostException exception) when (exception.Failure == ShareHostFailure.SignedOut)
        {
            cancel.Token.ThrowIfCancellationRequested();
            await SignInToShareHostAsync(host, client, cancel);
        }

        ShareHostProgress = progress;
        try
        {
            return await send();
        }
        catch (ShareHostException exception) when (exception.Failure == ShareHostFailure.SignedOut)
        {
            client.SignOut(host);
            throw;
        }
    }

    /// <summary>Publishes the upload. A new listing sends the draft of the editor, which then has to pass the checks.</summary>
    [RelayCommand]
    private async Task PublishToShareHostAsync()
    {
        if (_shareUpload is not { } shared || _share is not { } share || _owner.Services is not { } services || !CanPublishToShareHost)
            return;

        var listing = shared.ListingId is null ? Draft : null;
        if (listing is not null && !CanOpenPullRequest)
        {
            ShareHostError = Localization.ShareOtherFields;
            return;
        }

        // A sign-in between two sends can take minutes while both fields stay editable, and only the values of the click go out.
        var version = ShareHostVersion.Trim();
        var status = ShareHostStatus;
        using var cancel = new CancellationTokenSource();
        _shareHostRun = cancel;
        _publishRun = cancel;
        ShareHostError = null;
        try
        {
            var client = services.ShareHostClient;
            var published = await SendSignedInAsync(shared.Host, client, cancel, Localization.ListingShareHostPublishing, () => client.PublishAsync(shared.Host, shared.Upload.Id, version, status, listing, cancel.Token));
            if (_shareHostRun != cancel)
                return;

            _shareUpload = null;
            _sharePublished = (ShareListingName, shared.Host, published.Page, listing is null);
            SharePullRequest = published.PullRequest;
            IsShareDetailsOpen = false;
            RefreshShareRun();
            ShareFlowStep = ShareFlowStep.Done;

            // A publish without an id in its answer is a new version, which always has the id of the listing on the host.
            var id = published.Id ?? shared.ListingId!;
            await services.SharedContent.RecordAsync(
                new SharedContentEntry(share.Entry.Kind.ToContentType(), shared.Host.Key, shared.Host.Name, id, share.Entry.FolderName, published.Version, published.Page),
                listing is null ? null : new SharedContentAuthor(listing.Authors, listing.License));
            await _owner.ShowSharedContentAsync(share.Entry.Kind);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (ShareHostException exception) when (_shareHostRun == cancel)
        {
            await ShowShareHostFailureAsync(shared.Host, share, exception, version);
        }
        finally
        {
            if (_shareHostRun == cancel)
            {
                _shareHostRun = null;
                ShareHostProgress = null;
            }
        }
    }

    /// <summary>Stops the running step, or drops a waiting upload.</summary>
    [RelayCommand]
    private void CancelShareHost()
    {
        if (_shareHostRun is null)
        {
            ForgetShareHostRun();
            return;
        }

        _shareHostRun.Cancel();
        _shareHostRun = null;
        _shareHostCode = null;
        ShareHostProgress = null;

        // A running share has only an error that a page could not be opened, which ends with the run.
        ShareHostError = null;
        RefreshShareHostCode();
    }

    /// <summary>Opens the rename that a failed check of the name or the folder offers, and checks with the same host again after it.</summary>
    [RelayCommand]
    private void RenameForShare()
    {
        if (_share is not { } share || _checkedHost is not { } host)
            return;

        _owner.BeginShareRename(share, ShareFolderCheckText ?? Localization.FormatGameSaveShareRename(share.Content.Name), renamed =>
        {
            CheckAgainAfterRename(share, renamed, host);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// A short name that the player typed stays, and a new version keeps its listing, so the name decides the short name only while it still follows from the old name.
    /// The page takes clicks while the instance is listed again. Another share leaves this rename behind, and a step that moved takes the new name, because the folder moved, and checks nothing.
    /// </summary>
    private void CheckAgainAfterRename(GameSaveShareStart share, GameSaveEntry renamed, ShareHost host)
    {
        if (_share != share)
            return;

        _share = share with { Entry = renamed, Content = share.Content with { Name = renamed.Name } };
        var followsTheName = !IsNewVersionShare && Id == ListingShareId.From(share.Content.Name);
        Name = renamed.Name;
        if (followsTheName)
            Id = ListingShareId.From(renamed.Name);
        RefreshShare();

        if (!IsShareStep || ShareFlowStep != ShareFlowStep.Publish)
            return;

        // The new short name can be taken, and the step that shows why is the one before.
        if (!CanContinueShare)
        {
            BackToShareDescribe();
            return;
        }

        if (ShareHostRows.FirstOrDefault(row => row.Host == host) is { } same)
        {
            SelectedShareHostRow = same;
            ShareCheckAfterRename = UploadToShareHostCommand.ExecuteAsync(same);
        }
    }

    [RelayCommand]
    private void ToggleShareDetails() => IsShareDetailsOpen = !IsShareDetailsOpen;

    [RelayCommand]
    private void EditShareVersion() => IsEditingShareVersion = true;

    [RelayCommand]
    private async Task CopyShareHostCodeAndOpenAsync()
    {
        if (_shareHostCode is not { } code)
            return;

        await CopyShareHostCodeAsync();
        OpenShareHostPage(code.Page);
    }

    [RelayCommand]
    private async Task CopyShareHostCodeAsync()
    {
        if (_shareHostCode is { } code && _owner.WindowServices is { } window)
            await window.CopyTextAsync(code.Code);
    }

    [RelayCommand]
    private void OpenShareHostCodePage()
    {
        if (_shareHostCode is not { CompletePage: { } page } code || OpenShareHostPage(page))
            return;

        // A system that opens no browser leaves the address to type, which the step names only when no page has the code filled in.
        _shareHostCode = code with { CompletePage = null };
        RefreshShareHostCode();
    }

    [RelayCommand]
    private void OpenShareHostRules(ShareHost? host)
    {
        if (host?.Policy is { } policy)
            OpenShareHostPage(policy.AbsoluteUri);
    }

    private bool OpenShareHostPage(string url)
    {
        if (_owner.OpenListingPage(url) is not { } error)
            return true;

        ShareHostError = Localization.FormatListingShareHostOpenFailed(error);
        return false;
    }

    private void ShowShareChecks(IReadOnlyList<ShareCheck> checks)
    {
        _shareChecks = checks;

        // The API allows two checks that read the same, and Arrange takes each row only once.
        MainViewModel.Arrange(ShareHostFailedChecks, [.. checks.Where(check => !check.Passed).Select(CheckText).Distinct()]);
        MainViewModel.Arrange(ShareHostPassedChecks, [.. checks.Where(check => check.Passed).Select(CheckText).Distinct()]);
        OnPropertyChanged(nameof(HasShareDetails));
        OnPropertyChanged(nameof(ShowsShareRename));
        OnPropertyChanged(nameof(ShareFolderCheckText));
    }

    private string CheckText(ShareCheck check) => !check.Passed
        ? Localization.FormatListingShareCheckFailed(check.Name, check.Message ?? string.Empty)
        : check.Message is { Length: > 0 } message ? Localization.FormatShareCheckDetail(check.Name, message) : Localization.FormatListingShareCheckPassed(check.Name);

    /// <summary>Shows a refused or failed request with its way forward.</summary>
    /// <param name="version">The version that a publish sent, or null for an upload.</param>
    private async Task ShowShareHostFailureAsync(ShareHost host, GameSaveShareStart share, ShareHostException exception, string? version)
    {
        if (exception.Checks is { Count: > 0 } checks)
            ShowShareChecks(checks);

        // The host no longer has this upload, so Publish could only fail again and the step offers a new check.
        if (exception.Error == "upload_expired")
            ForgetShareHostRun();

        // A check can fail at the publish after it passed at the upload, and an upload with a failed check is not kept.
        if (ShareHostFailedChecks.Count > 0)
        {
            _shareUpload = null;
            RefreshShareRun();
        }

        var (text, detail, action) = ShareHostFailureText(host, exception, version);
        ShareHostError = text;
        ShareHostErrorDetail = detail;
        _shareErrorAction = action;
        RefreshShareErrorAction();

        switch (exception.Error)
        {
            case "version_taken":
                IsEditingShareVersion = true;
                break;
            case "id_taken":
                _refusedShareId = Draft.Id;
                Refresh();
                break;
            case "not_owner" when _owner.Services is { } services:
                await services.SharedContent.RemoveAsync(share.Entry.Kind.ToContentType(), host.Key, share.Entry.FolderName);
                await _owner.ShowSharedContentAsync(share.Entry.Kind);
                break;
        }
    }

    /// <summary>
    /// The text in the language of the player, the message of the host below it and the one action. A publish without a clear
    /// answer can have gone through, so it has its own text, and Publish stays because an upload cannot publish twice.
    /// </summary>
    private (string Text, string? Detail, ShareErrorAction Action) ShareHostFailureText(ShareHost host, ShareHostException exception, string? version) => exception switch
    {
        { Failure: ShareHostFailure.SignedOut } => (Localization.FormatListingShareHostSignedOut(host.Name), null, ShareErrorAction.None),
        { Failure: ShareHostFailure.SignInRefused } => (Localization.FormatListingShareHostSignInRefused(host.Name), null, ShareErrorAction.None),
        { Failure: not ShareHostFailure.Host } when version is not null => (Localization.FormatShareErrorLostAnswer(host.Name), null, ShareErrorAction.OpenHost),
        { IsServerError: true } when version is not null => (Localization.FormatShareErrorLostAnswer(host.Name), exception.Message, ShareErrorAction.OpenHost),
        { Failure: ShareHostFailure.NetworkError } => (Localization.FormatListingShareHostNetwork(host.Name), null, ShareErrorAction.None),
        { Failure: ShareHostFailure.Unavailable } => (Localization.FormatShareErrorUnavailable(host.Name), null, ShareErrorAction.None),
        { Failure: not ShareHostFailure.Host } => (Localization.FormatListingShareHostUnexpected(host.Name), null, ShareErrorAction.None),
        { Error: "id_taken" } => (Localization.FormatShareErrorIdTaken(Draft.Id, host.Name), exception.Message, ShareErrorAction.BackToDescription),
        { Error: "version_taken" } when ModVersion.TryParse(version, out var taken) =>
            (Localization.FormatShareErrorVersionTaken(ShareListingName, version!, new ModVersion(taken.Major, taken.Minor + 1, 0).ToString()), exception.Message, ShareErrorAction.None),
        { Error: "not_owner" } => (Localization.FormatShareErrorNotOwner(host.Name, ShareListingName), exception.Message, ShareErrorAction.SignOut),

        // FormatDuration rounds down, so a wait of some seconds would read as 0 minutes.
        { Error: "rate_limited", RetryAfter: { } wait } =>
            (Localization.FormatShareErrorRateLimited(host.Name, Localization.FormatDuration(TimeSpan.FromMinutes(Math.Ceiling(wait.TotalMinutes)))), exception.Message, ShareErrorAction.None),
        { Error: "rate_limited" } => (Localization.FormatShareErrorRateLimitedLater(host.Name), exception.Message, ShareErrorAction.None),
        { Error: "upload_expired" } => (Localization.FormatShareErrorUploadGone(host.Name), exception.Message, ShareErrorAction.OpenHost),
        { Error: "too_large" } => (Localization.FormatShareErrorTooLarge(host.Name), exception.Message, ShareErrorAction.None),
        { Error: "type_not_accepted" } => (IsSave ? Localization.FormatShareErrorNoSaves(host.Name) : Localization.FormatShareErrorNoVehicles(host.Name), exception.Message, ShareErrorAction.None),
        { Error: "invalid_request", FieldErrors.Count: > 0 } =>
            (Localization.FormatListingShareHostSaid(host.Name, exception.Message), string.Join('\n', exception.FieldErrors!.Values.SelectMany(lines => lines).Distinct()), ShareErrorAction.None),

        // The failed checks already carry the messages of the host.
        _ when ShareHostFailedChecks.Count > 0 => (Localization.FormatListingShareHostChecksFailed(host.Name), null, ShareErrorAction.None),
        { Error: "invalid_archive" } => (Localization.FormatListingShareHostChecksFailed(host.Name), exception.Message, ShareErrorAction.None),
        _ => (Localization.FormatListingShareHostSaid(host.Name, exception.Message), null, ShareErrorAction.None),
    };

    [RelayCommand]
    private void RunShareErrorAction()
    {
        switch (_shareErrorAction)
        {
            case ShareErrorAction.BackToDescription:
                BackToShareDescribe();
                break;
            case ShareErrorAction.SignOut when _checkedHost is { } host:
                SignOutOf(host);
                break;
            case ShareErrorAction.OpenHost when _checkedHost is { } host:
                // The text of the error stays, because it says what the player can do without the page.
                if (_owner.OpenListingPage(host.Page.AbsoluteUri) is { } error)
                    ShareHostErrorDetail = Localization.FormatListingShareHostOpenFailed(error);
                break;
        }
    }

    [RelayCommand]
    private void SignOutOfShareHost()
    {
        if (SelectedShareHostRow is { } row)
            SignOutOf(row.Host);
    }

    /// <summary>An upload belongs to the account that sent it, so it goes with the sign-in.</summary>
    private void SignOutOf(ShareHost host)
    {
        if (_owner.Services is not { } services)
            return;

        services.ShareHostClient.SignOut(host);
        ForgetShareHostRun();
    }
}
