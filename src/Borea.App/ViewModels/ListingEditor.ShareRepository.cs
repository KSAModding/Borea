using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Sharing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// A share from the player's own GitHub repository (RFC 0094): Borea makes a release with the archive in ksa-shares of the signed-in
/// account, names it under [releases] with a tag prefix of the listing (RFC 0096), and opens the listing pull request as for a mod.
/// </summary>
public sealed partial class ListingEditor
{
    internal const string NewShareRepositoryPage = "https://github.com/new?name=" + ShareRepository.Name;

    private long? _ownRepositoryId;

    public bool OffersOwnRepository => HasShare && CanSignIn;

    [ObservableProperty]
    private string? _ownRepositoryProgress;

    [ObservableProperty]
    private string? _ownRepositoryError;

    [ObservableProperty]
    private string? _ownRepositoryOutcome;

    /// <summary>What the player has to do on GitHub first: a repository the App may not create, or a repository it is not allowed on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsShareRepository), nameof(NeedsAppOnShareRepository))]
    private ShareRepositoryFailure? _ownRepositoryStep;

    public bool NeedsShareRepository => OwnRepositoryStep == ShareRepositoryFailure.MayNotCreateRepository;

    public bool NeedsAppOnShareRepository => OwnRepositoryStep is ShareRepositoryFailure.MayNotCreateRepository or ShareRepositoryFailure.AppNotOnRepository;

    /// <summary>The tag prefix of the listing in the share repository. An id has no slash, so the prefix of one listing never starts another.</summary>
    private string OwnTagPrefix => Id.Trim() + "/v";

    [RelayCommand]
    private async Task PublishFromOwnRepositoryAsync()
    {
        if (_share is not { } share || _owner.Services is not { } services || OwnRepositoryProgress is not null || IsPublishing)
            return;

        OwnRepositoryError = null;
        OwnRepositoryOutcome = null;
        OwnRepositoryStep = null;
        if (!CanOpenPullRequest)
        {
            OwnRepositoryError = Localization.ListingFixErrors;
            return;
        }

        if (!IsSignedIn && !await _owner.SignInToGitHubAsync(forListing: true))
            return;

        OwnRepositoryProgress = Localization.ListingShareHostWriting;
        string? file = null;
        try
        {
            if (await _owner.WriteShareArchiveToTempAsync(share) is not { } written || _share != share)
                return;

            file = written.File;
            var repository = await services.ShareRepository.EnsureAsync();
            var prefix = OwnTagPrefix;
            var version = await services.ShareRepository.NextVersionAsync(repository, prefix);
            OwnRepositoryProgress = Localization.FormatListingShareOwnReleasing(prefix + version);
            await using (var archive = File.OpenRead(file))
                await services.ShareRepository.PublishAsync(repository, prefix, version, $"{Id.Trim()}-{version}.zip", archive);

            if (_share != share)
                return;

            OwnRepositoryOutcome = Localization.FormatListingShareOwnReleased(prefix + version, repository.FullName);
            ReleasesGitHub = repository.FullName;
            ReleasesTagPrefix = prefix;

            // A listing that already names this repository takes the new release from the watcher, so it needs no pull request.
            if (!IsListedFrom(repository))
                await PublishCommand.ExecuteAsync(null);
        }
        catch (ShareRepositoryException exception) when (_share == share)
        {
            _ownRepositoryId = exception.RepositoryId;
            OwnRepositoryStep = exception.Failure is ShareRepositoryFailure.MayNotCreateRepository or ShareRepositoryFailure.AppNotOnRepository ? exception.Failure : null;
            OwnRepositoryError = exception.Failure switch
            {
                ShareRepositoryFailure.MayNotCreateRepository => Localization.ListingShareOwnMayNotCreate,
                ShareRepositoryFailure.AppNotOnRepository => Localization.FormatListingShareOwnAppNotOnRepository(_owner.GitHubLogin + "/" + ShareRepository.Name),
                ShareRepositoryFailure.SignedOut => Localization.ListingErrorSignedOut,
                ShareRepositoryFailure.RateLimited or ShareRepositoryFailure.NetworkError => Localization.ListingErrorNetwork,
                _ => Localization.FormatListingShareOwnRefused(exception.Detail ?? exception.Failure.ToString()),
            };
        }
        catch (IOException exception) when (_share == share)
        {
            OwnRepositoryError = exception.Message;
        }
        finally
        {
            if (file is not null)
                MainViewModel.TryDeleteFile(file);
            OwnRepositoryProgress = null;
        }
    }

    [RelayCommand]
    private void OpenNewShareRepository()
    {
        if (_owner.OpenListingPage(NewShareRepositoryPage) is { } error)
            OwnRepositoryError = Localization.FormatListingOpenFailed(error);
    }

    [RelayCommand]
    private void AllowAppOnShareRepository()
    {
        var url = _ownRepositoryId is { } id ? _owner.Services?.GitHub.InstallUrlFor(id) : _owner.Services?.GitHub.InstallUrl;
        if (url is not null && _owner.OpenListingPage(url) is { } error)
            OwnRepositoryError = Localization.FormatListingOpenFailed(error);
    }

    private void ForgetOwnRepositoryRun()
    {
        _ownRepositoryId = null;
        OwnRepositoryError = null;
        OwnRepositoryOutcome = null;
        OwnRepositoryStep = null;
        OnPropertyChanged(nameof(OffersOwnRepository));
    }

    private bool IsListedFrom(ShareRepository repository)
    {
        var listings = Type == ListingFolders.SaveType ? _snapshot?.Saves : _snapshot?.Vehicles;
        return listings?.FirstOrDefault(listing => ModIds.Equals(listing.Id, Id.Trim()))?.Authored?.Releases?.Hosts
            .Any(host => host.Kind == ReleaseHostKind.GitHub && string.Equals(host.Reference, repository.FullName, StringComparison.OrdinalIgnoreCase)) == true;
    }
}
