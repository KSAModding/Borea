using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Dependencies;
using Borea.Core.Index;
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

    public bool OffersOwnRepository => HasShare && CanSignIn && FitsOwnRepository(_owner.GitHubLogin);

    /// <summary>A release is made once per share, so after it the player shares again from the instance page.</summary>
    public bool CanPublishFromOwnRepository => !IsSharing && OwnRepositoryOutcome is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSharing), nameof(CanUploadToShareHost), nameof(CanPublishToShareHost), nameof(CanPublishFromOwnRepository))]
    private string? _ownRepositoryProgress;

    [ObservableProperty]
    private string? _ownRepositoryError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublishFromOwnRepository))]
    private string? _ownRepositoryOutcome;

    /// <summary>The listing keeps dependencies that differ from the proposed ones, which only an edit of the listing changes.</summary>
    [ObservableProperty]
    private bool _ownListingNeedsEdit;

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
        if (_share is not { } share || _owner.Services is not { } services || !CanPublishFromOwnRepository || IsPublishing)
            return;

        OwnRepositoryError = null;
        OwnRepositoryStep = null;
        OwnListingNeedsEdit = false;

        // A listed share gets a new release, which the watcher stamps, so only a new listing needs a valid document.
        var listed = ListedShare() is not null;
        if (!listed && !CanOpenPullRequest)
        {
            OwnRepositoryError = Localization.ListingFixErrors;
            return;
        }

        if (!IsSignedIn && !await _owner.SignInToGitHubAsync(forListing: true))
            return;

        if (_share != share)
            return;

        if (_owner.GitHubLogin is not { } login || !FitsOwnRepository(login))
        {
            OwnRepositoryError = Localization.ListingShareOwnOtherHost;
            return;
        }

        // The draft must be valid with the repository and the prefix before the release exists, because a release whose
        // pull request cannot open would hold its version.
        var prefix = OwnTagPrefix;
        var (gitHub, tagPrefix) = (ReleasesGitHub, ReleasesTagPrefix);
        if (!listed)
        {
            ReleasesGitHub = login + "/" + ShareRepository.Name;
            ReleasesTagPrefix = prefix;
            if (!CanOpenPullRequest)
            {
                (ReleasesGitHub, ReleasesTagPrefix) = (gitHub, tagPrefix);
                OwnRepositoryError = Localization.ListingFixErrors;
                return;
            }
        }

        OwnRepositoryProgress = Localization.ListingShareHostWriting;
        string? file = null;
        var released = false;
        try
        {
            if (await _owner.WriteShareArchiveToTempAsync(share) is not { } written)
                return;

            file = written.File;
            if (_share != share)
                return;

            var repository = await services.ShareRepository.EnsureAsync();
            var version = await services.ShareRepository.NextVersionAsync(repository, prefix);
            OwnRepositoryProgress = Localization.FormatListingShareOwnReleasing(prefix + version);
            await using (var archive = File.OpenRead(file))
                await services.ShareRepository.PublishAsync(repository, prefix, version, $"{Id.Trim()}-{version}.zip", archive);

            released = true;
            if (_share != share)
                return;

            if (listed)
            {
                OwnRepositoryOutcome = Localization.FormatListingShareOwnNextVersion(prefix + version, repository.FullName);
                OwnListingNeedsEdit = !SameDependencies(ListedShare()?.Authored?.Dependencies ?? [], Draft.Dependencies);
                return;
            }

            OwnRepositoryOutcome = Localization.FormatListingShareOwnReleased(prefix + version, repository.FullName);
            ReleasesGitHub = repository.FullName;
            CheckOwnershipAgain();
            await PublishCommand.ExecuteAsync(null);
        }
        catch (ShareRepositoryException exception) when (_share == share)
        {
            _ownRepositoryId = exception.RepositoryId;
            OwnRepositoryStep = exception.Failure is ShareRepositoryFailure.MayNotCreateRepository or ShareRepositoryFailure.AppNotOnRepository ? exception.Failure : null;
            OwnRepositoryError = exception.Failure switch
            {
                ShareRepositoryFailure.MayNotCreateRepository => Localization.ListingShareOwnMayNotCreate,
                ShareRepositoryFailure.AppNotOnRepository => Localization.FormatListingShareOwnAppNotOnRepository(login + "/" + ShareRepository.Name),
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
            if (!released && !listed && _share == share)
                (ReleasesGitHub, ReleasesTagPrefix) = (gitHub, tagPrefix);
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

    /// <summary>Opens the listed document of the share, whose dependencies only a pull request changes.</summary>
    [RelayCommand]
    private async Task EditOwnListingAsync()
    {
        if (ListedShare() is not { } listing)
            return;

        SelectedListed = new ListedListing(listing.Id, listing.Authored?.Name ?? listing.Id, string.Empty, IsOwn: true, Type: Type);
        await LoadListedCommand.ExecuteAsync(null);
    }

    private void ForgetOwnRepositoryRun()
    {
        _ownRepositoryId = null;
        OwnRepositoryError = null;
        OwnRepositoryOutcome = null;
        OwnRepositoryStep = null;
        OwnListingNeedsEdit = false;
        OnPropertyChanged(nameof(OffersOwnRepository));
    }

    /// <summary>The snapshot entry of a vehicle or save with the id of the draft, or null for a new listing.</summary>
    private ContentIndexListing? ListedShare()
    {
        var listings = Type == ListingFolders.SaveType ? _snapshot?.Saves : _snapshot?.Vehicles;
        return listings?.FirstOrDefault(listing => ModIds.Equals(listing.Id, Id.Trim()));
    }

    /// <summary>
    /// Whether the share can live in ksa-shares of <paramref name="login"/>: the draft and the listed document name no other
    /// release host, and a listed document names that repository with the prefix of this listing. A null login checks the name only.
    /// </summary>
    private bool FitsOwnRepository(string? login)
    {
        if (ShareHost is not null || ReleasesSpaceDock.Trim().Length > 0
            || (ReleasesGitHub.Trim().Length > 0 && !IsShareRepository(ReleasesGitHub.Trim(), login))
            || (ReleasesTagPrefix.Trim().Length > 0 && ReleasesTagPrefix.Trim() != OwnTagPrefix))
        {
            return false;
        }

        if (ListedShare() is not { } listing)
            return true;

        var releases = listing.Authored?.Releases;
        return releases is not null
            && releases.Hosts.Count == 1
            && releases.Hosts[0].Kind == ReleaseHostKind.GitHub
            && IsShareRepository(releases.Hosts[0].Reference, login)
            && releases.TagPrefix == OwnTagPrefix;
    }

    private static bool IsShareRepository(string repository, string? login) =>
        repository.Split('/') is [var owner, var name]
        && string.Equals(name, ShareRepository.Name, StringComparison.OrdinalIgnoreCase)
        && (login is null || string.Equals(owner, login, StringComparison.OrdinalIgnoreCase));

    /// <summary>The same entries in any order. An entry with alternatives never counts as the same, so the player checks it.</summary>
    private static bool SameDependencies(IReadOnlyList<ModDependency> listed, IReadOnlyList<ListingDependency> proposed) =>
        listed.Count == proposed.Count
        && listed.All(entry => !entry.IsAnyOf && entry.ModId is not null)
        && proposed.All(entry => entry.Preserved is null)
        && listed.Select(entry => Key(entry.ModId!, entry.Kind.ToString(), entry.MinVersion?.ToString(), entry.MaxVersion?.ToString())).Order(StringComparer.Ordinal)
            .SequenceEqual(proposed.Select(entry => Key(entry.Id, entry.Kind, Normalized(entry.Min), Normalized(entry.Max))).Order(StringComparer.Ordinal));

    private static string Key(string id, string kind, string? min, string? max) => $"{id.ToLowerInvariant()} {kind.ToLowerInvariant()} {min} {max}";

    private static string? Normalized(string? version) =>
        string.IsNullOrWhiteSpace(version) ? null : ModVersion.TryParseAuthored(version, out var parsed) ? parsed.ToString() : version;
}
