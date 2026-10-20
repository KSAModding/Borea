using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

/// <summary>
/// A share on a host of RFC 0094: Borea writes the archive, signs in, uploads it with the dependencies, shows what the host
/// checked, and publishes the version and status the player chose. The host opens the listing pull request.
/// </summary>
public sealed partial class ListingEditor
{
    public static IReadOnlyList<string> ShareStatuses { get; } = ["stable", "testing", "dev"];

    private IReadOnlyList<ShareHost> _shareHosts = [];
    private CancellationTokenSource? _shareHostRun;
    private (ShareHost Host, ShareUpload Upload, string? ListingId)? _shareUpload;
    private (string Page, string Code)? _shareHostCode;

    /// <summary>The last reading of hosts.toml, which tests wait for.</summary>
    internal Task ShareHostsLoad { get; private set; } = Task.CompletedTask;

    public ObservableCollection<ShareHostRow> ShareHostRows { get; } = [];

    public bool HasShareHosts => HasShare && ShareHostRows.Count > 0;

    public ObservableCollection<string> ShareHostChecks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublishToShareHost), nameof(CanUploadToShareHost))]
    private string? _shareHostProgress;

    [ObservableProperty]
    private string? _shareHostError;

    [ObservableProperty]
    private string? _shareHostListingText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublishToShareHost))]
    private string _shareHostVersion = string.Empty;

    [ObservableProperty]
    private string _shareHostStatus = ShareStatuses[0];

    [ObservableProperty]
    private string? _shareHostOutcome;

    [ObservableProperty]
    private Uri? _sharePullRequest;

    public string? ShareHostCodeText => _shareHostCode is { } code ? Localization.FormatListingShareHostCode(code.Page, code.Code) : null;

    public bool HasShareUpload => _shareUpload is not null;

    public bool CanUploadToShareHost => ShareHostProgress is null;

    /// <summary>A check that failed means the host refuses to publish, so the player shares again after a fix.</summary>
    public bool CanPublishToShareHost => _shareUpload is { } upload && upload.Upload.Checks.All(check => check.Passed) && ShareHostVersion.Trim().Length > 0 && ShareHostProgress is null;

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
    }

    private void RefreshShareHostRows()
    {
        var rows = _share is null ? [] : _shareHosts.Where(host => host.Accepts(Type)).Select(host => new ShareHostRow(host, Localization.FormatListingShareOnHost(host.Name))).ToList();
        MainViewModel.Arrange(ShareHostRows, rows);
        if (_shareUpload is { } shared)
            MainViewModel.Arrange(ShareHostChecks, shared.Upload.Checks.Select(CheckText).ToList());
        OnPropertyChanged(nameof(HasShareHosts));
        OnPropertyChanged(nameof(ShareHostCodeText));
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
        using var cancel = new CancellationTokenSource();
        _shareHostRun = cancel;
        ShareHostProgress = Localization.ListingShareHostWriting;
        string? file = null;
        try
        {
            if (await _owner.WriteShareArchiveToTempAsync(share) is not { } written || _shareHostRun != cancel)
                return;

            file = written.File;
            if (host.MaxBytesFor(Type) is { } max && written.Archive.Size > max)
            {
                ShareHostError = Localization.FormatListingShareHostTooLarge(written.Archive.Size.ToString("N0", CultureInfo.CurrentCulture), host.Name, max.ToString("N0", CultureInfo.CurrentCulture));
                return;
            }

            if (!services.ShareHostClient.IsSignedIn(host))
            {
                ShareHostProgress = Localization.FormatListingShareHostSigningIn(host.Name);
                var progress = new Progress<GitHubDeviceCode>(code =>
                {
                    if (_shareHostRun != cancel)
                        return;

                    _shareHostCode = (code.VerificationUri, code.UserCode);
                    OnPropertyChanged(nameof(ShareHostCodeText));
                });
                await services.ShareHostClient.SignInAsync(host, progress, _owner.StaySignedInToGitHub, cancel.Token);
                _shareHostCode = null;
                OnPropertyChanged(nameof(ShareHostCodeText));
            }

            ShareHostProgress = Localization.ListingShareHostUploading;
            var listingId = ListingOnHost(host);
            ShareUpload upload;
            await using (var archive = File.OpenRead(file))
                upload = await services.ShareHostClient.UploadAsync(host, archive, share.Content.Name + ".zip", Draft.Dependencies, listingId, cancel.Token);

            if (_shareHostRun != cancel)
                return;

            _shareUpload = (host, upload, listingId);
            MainViewModel.Arrange(ShareHostChecks, upload.Checks.Select(CheckText).ToList());
            ShareHostListingText = upload.ListingText;
            ShareHostVersion = upload.ProposedVersion ?? string.Empty;
            if (upload.Checks.Any(check => !check.Passed))
                ShareHostError = Localization.ListingShareHostChecksFailed;
            OnPropertyChanged(nameof(HasShareUpload));
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (ShareHostException exception) when (_shareHostRun == cancel)
        {
            ShareHostError = ShareHostErrorText(host, exception);
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
                _shareHostCode = null;
                ShareHostProgress = null;
                OnPropertyChanged(nameof(ShareHostCodeText));
            }
        }
    }

    /// <summary>Publishes the upload. A new listing sends the file of the editor, which then has to pass the checks.</summary>
    [RelayCommand]
    private async Task PublishToShareHostAsync()
    {
        if (_shareUpload is not { } shared || _owner.Services is not { } services || !CanPublishToShareHost)
            return;

        var listingText = shared.ListingId is null ? DocumentText : null;
        if (listingText is not null && !CanOpenPullRequest)
        {
            ShareHostError = Localization.ListingFixErrors;
            return;
        }

        using var cancel = new CancellationTokenSource();
        _shareHostRun = cancel;
        ShareHostError = null;
        ShareHostProgress = Localization.ListingShareHostPublishing;
        try
        {
            var published = await services.ShareHostClient.PublishAsync(shared.Host, shared.Upload.Id, ShareHostVersion.Trim(), ShareHostStatus, listingText, cancel.Token);
            if (_shareHostRun != cancel)
                return;

            _shareUpload = null;
            OnPropertyChanged(nameof(HasShareUpload));
            SharePullRequest = published.PullRequest;
            ShareHostOutcome = Localization.FormatListingShareHostPublished(published.Id, published.Version);
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

    [RelayCommand]
    private void CancelShareHost()
    {
        _shareHostRun?.Cancel();
        _shareHostRun = null;
        _shareHostCode = null;
        ShareHostProgress = null;
        OnPropertyChanged(nameof(ShareHostCodeText));
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

    private string CheckText(ShareCheck check) => check.Passed
        ? Localization.FormatListingShareCheckPassed(check.Name)
        : Localization.FormatListingShareCheckFailed(check.Name, check.Message ?? string.Empty);

    private string ShareHostErrorText(ShareHost host, ShareHostException exception) => exception.Failure switch
    {
        ShareHostFailure.Host => exception.Message,
        ShareHostFailure.SignedOut => Localization.ListingShareHostSignedOut,
        ShareHostFailure.SignInRefused => Localization.ListingShareHostSignInRefused,
        ShareHostFailure.NetworkError => Localization.ListingShareHostNetwork,
        _ => Localization.FormatListingShareHostUnexpected(host.Name),
    };
}
