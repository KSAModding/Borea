using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Sharing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>A share host that takes the type of the shared item, as a button of the share card.</summary>
public sealed record ShareHostRow(ShareHost Host, string Label);

/// <summary>One check that the host ran on the upload.</summary>
public sealed record ShareCheckRow(string Text, bool Passed)
{
    public bool Failed => !Passed;
}

/// <summary>A release status that the host takes, with the name the App shows for it.</summary>
public sealed record ShareStatusChoice(string Value, string Label);

/// <summary>
/// A share on a host of RFC 0094: Borea writes the archive, signs in, uploads it with the dependencies, shows what the host
/// checked, and publishes the version and status the player chose. The host opens the listing pull request.
/// </summary>
public sealed partial class ListingEditor
{
    private IReadOnlyList<ShareHost> _shareHosts = [];
    private CancellationTokenSource? _shareHostRun;
    private (ShareHost Host, ShareUpload Upload, string? ListingId)? _shareUpload;
    private (string Page, string Code)? _shareHostCode;
    private (bool IndexOnly, bool Listing) _shownCards = (true, true);

    /// <summary>The sections whose values a share host does not take.</summary>
    private static readonly string[] IndexOnlySections = ["links", "releases", "compatibility", "images"];

    /// <summary>The last reading of hosts.toml, which tests wait for.</summary>
    internal Task ShareHostsLoad { get; private set; } = Task.CompletedTask;

    public ObservableCollection<ShareHostRow> ShareHostRows { get; } = [];

    public bool HasShareHosts => HasShare && ShareHostRows.Count > 0;

    public ObservableCollection<ShareCheckRow> ShareHostChecks { get; } = [];

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
    [NotifyPropertyChangedFor(nameof(CanPublishToShareHost), nameof(CanUploadToShareHost), nameof(IsSharing))]
    private string? _shareHostProgress;

    [ObservableProperty]
    private string? _shareHostError;

    [ObservableProperty]
    private string? _shareHostListingText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublishToShareHost))]
    private string _shareHostVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedShareStatus))]
    private string _shareHostStatus = "stable";

    [ObservableProperty]
    private string? _shareHostOutcome;

    [ObservableProperty]
    private Uri? _sharePullRequest;

    public string? ShareHostCodeText => _shareHostCode is { } code ? Localization.FormatListingShareHostCode(code.Page) : null;

    public string? ShareHostUserCode => _shareHostCode?.Code;

    /// <summary>An upload that the host can publish. One with a failed check is not kept, so the player shares again after a fix.</summary>
    public bool HasShareUpload => _shareUpload is not null;

    /// <summary>A share on a host runs.</summary>
    public bool IsSharing => ShareHostProgress is not null;

    public bool CanUploadToShareHost => !IsSharing;

    /// <summary>
    /// False from the upload to a share host on, because the host takes no links, releases, compatibility or images. The cards
    /// of them hide and keep their values, for the pull request path.
    /// </summary>
    public bool ShowsIndexOnlyCards => !IsSharing && !HasShareUpload && ShareHostOutcome is null;

    /// <summary>False while an upload for a new version waits, because that publish sends no listing, so the about and tags cards hide too.</summary>
    public bool ShowsListingCards => _shareUpload?.ListingId is null;

    public bool CanPublishToShareHost => _shareUpload is not null && ShareHostVersion.Trim().Length > 0 && !IsSharing;

    private void StartShareHosts()
    {
        ForgetShareHostRun();
        _shareHosts = [];
        RefreshShareHostRows();
        if (_owner.Services is { } services)
            ShareHostsLoad = LoadShareHostsAsync(services.ShareHosts, _share);
    }

    private async Task LoadShareHostsAsync(IShareHostSource source, GameSaveShareStart? share)
    {
        var hosts = await source.GetAsync();
        if (_share != share)
            return;

        _shareHosts = hosts;
        RefreshShareHostRows();
        OnPropertyChanged(nameof(ShareHostText));
    }

    private void RefreshShareHostRows()
    {
        var rows = _share is null ? [] : _shareHosts.Where(host => host.Accepts(Type)).Select(host => new ShareHostRow(host, Localization.FormatListingShareOnHost(host.Name))).ToList();
        MainViewModel.Arrange(ShareHostRows, rows);
        if (_shareUpload is { } shared)
            MainViewModel.Arrange(ShareHostChecks, CheckRows(shared.Upload.Checks));
        OnPropertyChanged(nameof(HasShareHosts));
        OnPropertyChanged(nameof(ShareHostCodeText));
        OnPropertyChanged(nameof(ShareHostUserCode));
    }

    /// <summary>Drops a running share and its result, because they belong to the item before.</summary>
    private void ForgetShareHostRun()
    {
        _shareHostRun?.Cancel();
        _shareHostRun = null;
        _shareUpload = null;
        _shareHostCode = null;
        ShareHostProgress = null;
        ShareHostError = null;
        ShareHostListingText = null;
        ShareHostOutcome = null;
        SharePullRequest = null;
        ShareHostChecks.Clear();
        OnPropertyChanged(nameof(HasShareUpload));
        OnPropertyChanged(nameof(CanPublishToShareHost));
        OnPropertyChanged(nameof(ShareHostCodeText));
        OnPropertyChanged(nameof(ShareHostUserCode));
        RefreshShownCards();
    }

    /// <summary>Shows or hides the cards and their steps when the share host path starts or ends.</summary>
    private void RefreshShownCards()
    {
        if (_shownCards == (ShowsIndexOnlyCards, ShowsListingCards))
            return;

        _shownCards = (ShowsIndexOnlyCards, ShowsListingCards);
        OnPropertyChanged(nameof(ShowsIndexOnlyCards));
        OnPropertyChanged(nameof(ShowsListingCards));
        Refresh();
    }

    partial void OnShareHostProgressChanged(string? value) => RefreshShownCards();

    partial void OnShareHostOutcomeChanged(string? value) => RefreshShownCards();

    /// <summary>
    /// Writes the archive and refuses one above the limit of the host before any request. Then it signs in when needed and
    /// uploads, and shows what the host checked with the version it proposes. An error in a card that hides from the upload on
    /// stops it first, while the player can still see the field.
    /// </summary>
    [RelayCommand]
    private async Task UploadToShareHostAsync(ShareHostRow? row)
    {
        if (row is null || _share is not { } share || _owner.Services is not { } services || !CanUploadToShareHost)
            return;

        var host = row.Host;
        ForgetShareHostRun();
        if (ListingOnHost(host) is null && Errors.Any(issue => IndexOnlySections.Contains(SectionOf(issue))))
        {
            ShareHostError = Localization.ListingFixErrors;
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
                ShareHostError = Localization.FormatListingShareHostTooLarge(MainViewModel.SizeText(written.Archive.Size), host.Name, MainViewModel.SizeText(max));
                return;
            }

            var client = services.ShareHostClient;
            if (!client.IsSignedIn(host))
                await SignInToShareHostAsync(host, client, cancel);

            var listingId = ListingOnHost(host);
            var upload = await SendSignedInAsync(host, client, cancel, Localization.ListingShareHostUploading, async () =>
            {
                await using var archive = File.OpenRead(file);
                return await client.UploadAsync(host, archive, share.Content.Name + ".zip", Draft.Dependencies, listingId, cancel.Token);
            });

            if (_shareHostRun != cancel)
                return;

            MainViewModel.Arrange(ShareHostChecks, CheckRows(upload.Checks));
            ShareHostListingText = upload.ListingText;
            if (upload.Checks.All(check => check.Passed))
            {
                _shareUpload = (host, upload, listingId);
                ShareHostVersion = upload.ProposedVersion ?? string.Empty;
            }
            else
            {
                ShareHostError = Localization.ListingShareHostChecksFailed;
            }

            OnPropertyChanged(nameof(HasShareUpload));
            RefreshShownCards();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (ShareHostException exception) when (_shareHostRun == cancel)
        {
            ShareHostError = ShareHostErrorText(host, exception);
            if (exception.Checks is { Count: > 0 } checks)
                MainViewModel.Arrange(ShareHostChecks, CheckRows(checks));
        }
        catch (IOException exception) when (_shareHostRun == cancel)
        {
            ShareHostError = exception.Message;
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

    /// <summary>Signs in and shows the code that the player enters on the host until the sign-in ends.</summary>
    private async Task SignInToShareHostAsync(ShareHost host, IShareHostClient client, CancellationTokenSource cancel)
    {
        ShareHostProgress = Localization.FormatListingShareHostSigningIn(host.Name);
        var progress = new Progress<GitHubDeviceCode>(code =>
        {
            if (_shareHostRun != cancel)
                return;

            _shareHostCode = (code.VerificationUri, code.UserCode);
            OnPropertyChanged(nameof(ShareHostCodeText));
            OnPropertyChanged(nameof(ShareHostUserCode));
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
                _shareHostCode = null;
                OnPropertyChanged(nameof(ShareHostCodeText));
                OnPropertyChanged(nameof(ShareHostUserCode));
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
        if (_shareUpload is not { } shared || _owner.Services is not { } services || !CanPublishToShareHost)
            return;

        var listing = shared.ListingId is null ? Draft : null;
        if (listing is not null && !CanOpenPullRequest)
        {
            ShareHostError = Localization.ListingFixErrors;
            return;
        }

        // A sign-in between two sends can take minutes while both fields stay editable, and only the values of the click go out.
        var version = ShareHostVersion.Trim();
        var status = ShareHostStatus;
        using var cancel = new CancellationTokenSource();
        _shareHostRun = cancel;
        ShareHostError = null;
        try
        {
            var client = services.ShareHostClient;
            var published = await SendSignedInAsync(shared.Host, client, cancel, Localization.ListingShareHostPublishing, () => client.PublishAsync(shared.Host, shared.Upload.Id, version, status, listing, cancel.Token));
            if (_shareHostRun != cancel)
                return;

            _shareUpload = null;
            OnPropertyChanged(nameof(HasShareUpload));
            SharePullRequest = published.PullRequest;

            // A publish without an id in its answer is a new version, which always has the id of the listing on the host.
            var id = published.Id ?? shared.ListingId!;
            ShareHostOutcome = published.PullRequest is null
                ? Localization.FormatListingShareHostReleased(id, published.Version)
                : Localization.FormatListingShareHostPublished(id, published.Version);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (ShareHostException exception) when (_shareHostRun == cancel)
        {
            ShareHostError = ShareHostErrorText(shared.Host, exception);
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

    /// <summary>Stops the running step, or drops a waiting upload, which shows every card again.</summary>
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
        OnPropertyChanged(nameof(ShareHostCodeText));
        OnPropertyChanged(nameof(ShareHostUserCode));
    }

    [RelayCommand]
    private async Task CopyShareHostCodeAndOpenAsync()
    {
        if (_shareHostCode is not { } code)
            return;

        if (_owner.WindowServices is { } window)
            await window.CopyTextAsync(code.Code);
        if (_owner.OpenListingPage(code.Page) is { } error)
            ShareHostError = Localization.FormatListingOpenFailed(error);
    }

    [RelayCommand]
    private void OpenSharePullRequest()
    {
        if (SharePullRequest is { } url && _owner.OpenListingPage(url.AbsoluteUri) is { } error)
            ShareHostError = Localization.FormatListingOpenFailed(error);
    }

    /// <summary>The id of the listed vehicle or save on <paramref name="host"/> that the draft names, for a new version of it, or null for a new listing.</summary>
    private string? ListingOnHost(ShareHost host)
    {
        var listings = Type == ListingFolders.SaveType ? _snapshot?.Saves : _snapshot?.Vehicles;
        return listings?.FirstOrDefault(listing => ModIds.Equals(listing.Id, Id.Trim()))?.Authored?.Releases?.Hosts
            .FirstOrDefault(entry => entry.Kind == ReleaseHostKind.Share && entry.Host == host.Key)?.Reference;
    }

    // The API allows two checks that read the same, and Arrange takes each row only once.
    private List<ShareCheckRow> CheckRows(IEnumerable<ShareCheck> checks) => [.. checks.Select(CheckRow).Distinct()];

    private ShareCheckRow CheckRow(ShareCheck check) => new(
        check.Passed ? Localization.FormatListingShareCheckPassed(check.Name) : Localization.FormatListingShareCheckFailed(check.Name, check.Message ?? string.Empty),
        check.Passed);

    private string ShareHostErrorText(ShareHost host, ShareHostException exception) => exception.Failure switch
    {
        ShareHostFailure.Host => Localization.FormatListingShareHostSaid(host.Name, exception.Message),
        ShareHostFailure.SignedOut => Localization.ListingShareHostSignedOut,
        ShareHostFailure.SignInRefused => Localization.ListingShareHostSignInRefused,
        ShareHostFailure.NetworkError => Localization.ListingShareHostNetwork,
        _ => Localization.FormatListingShareHostUnexpected(host.Name),
    };
}
