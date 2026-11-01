using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>Where a share stands while the page shows <see cref="ListingStep.Share"/>.</summary>
public enum ShareFlowStep
{
    Choose = 0,
    Describe = 1,
    Publish = 2,
    Done = 3,
}

/// <summary>A license of the plain list. A null value stands for another license, which the player types.</summary>
public sealed record ShareLicenseChoice(string? Value, string Label);

/// <summary>A listing on a share host that a share can add a version to. The index may not hold it yet.</summary>
/// <param name="HostName">The name of the host when the record of a share holds it, and otherwise the hosts of the index name it.</param>
/// <param name="Reference">The id of the listing on the host.</param>
/// <param name="Version">The version that is online, or null when Borea knows none.</param>
/// <param name="HoldsTheName">Whether an item that is not this listing cannot keep its name next to it.</param>
internal sealed record ShareListing(string Id, string Name, string HostKey, string? HostName, string Reference, string? Version, bool HoldsTheName);

/// <summary>
/// The steps of a share: plain fields, and nothing goes to a host before they are valid. They edit the same draft as the full form,
/// and going back from the publish step drops the upload, so what the upload sent can only change before it.
/// </summary>
public sealed partial class ListingEditor
{
    /// <summary>The licenses that a share offers in plain words, and the only place that names them.</summary>
    private static readonly (string Spdx, Func<LocalizationService, string> Label)[] ShareLicenses =
    [
        ("CC-BY-4.0", text => text.ShareLicenseCcBy),
        ("CC0-1.0", text => text.ShareLicenseCc0),
        ("CC-BY-SA-4.0", text => text.ShareLicenseCcBySa),
        ("CC-BY-NC-4.0", text => text.ShareLicenseCcByNc),
    ];

    /// <summary>The listing on a share host that the player said this item belongs to, which makes the share a new version of it, or null for a new listing.</summary>
    private ShareListing? _shareListing;

    /// <summary>The listing that the item looks like, until the player says whether it belongs to it.</summary>
    private ShareListing? _askedShareListing;

    /// <summary>The listings that the player said this item is not. A listing that installs under the name of the item still counts, because the checks of the index refuse that folder.</summary>
    private readonly HashSet<string> _declinedShareListings = new(ModIds.Comparer);

    /// <summary>The short name that the host refused at a publish, which the index can hold back only hours later.</summary>
    private string? _refusedShareId;

    private bool _choseOtherLicense;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShareChoose), nameof(IsShareDescribe), nameof(IsSharePublish), nameof(IsShareDone), nameof(ShowsPreviewCard))]
    private ShareFlowStep _shareFlowStep;

    [ObservableProperty]
    private bool _canContinueShare;

    /// <summary>Why Next is disabled, except for an error outside the step, which <see cref="ShareOtherFieldsText"/> says.</summary>
    [ObservableProperty]
    private string? _shareNextReason;

    [ObservableProperty]
    private bool _hasShareOtherFields;

    /// <summary>Which fields outside the step stop the share, in the words of the full form.</summary>
    [ObservableProperty]
    private string? _shareOtherFieldsText;

    [ObservableProperty]
    private string? _shareIdError;

    [ObservableProperty]
    private string? _shareSentenceError;

    [ObservableProperty]
    private string? _shareLicenseError;

    [ObservableProperty]
    private string _shareSentenceCount = string.Empty;

    [ObservableProperty]
    private bool _isShareMoreOptionsOpen;

    /// <summary>Whether the item names no mod at all. A proposed mod that the player removed, a note or a file that a mod wrote says that it may need one that the list does not name.</summary>
    [ObservableProperty]
    private bool _hasNoShareMods;

    public bool IsShareChoose => ShareFlowStep == ShareFlowStep.Choose;

    public bool IsShareDescribe => ShareFlowStep == ShareFlowStep.Describe;

    public bool IsSharePublish => ShareFlowStep == ShareFlowStep.Publish;

    public bool IsShareDone => ShareFlowStep == ShareFlowStep.Done;

    public string? ShareDoneHeadingText => _sharePublished is not { } done ? null
        : _isShareDoneTakenDown ? Localization.FormatShareTakenDownHeading(done.Name, done.Version)
        : Localization.FormatShareDoneHeading(done.Name);

    public string ShareDoneStateText => _isShareDoneTakenDown && _sharePublished is { } done ? Localization.FormatShareTakenDown(done.Version) : Localization.ShareDoneNow;

    /// <summary>When Discover shows it. A new listing without a request shows later, because the host still has to send that request.</summary>
    public string? ShareDoneDiscoverText => _sharePublished is not { } done || _isShareDoneTakenDown ? null
        : done.IsNewVersion ? Localization.ShareDoneNewVersion
        : SharePullRequest is null ? Localization.FormatShareDoneNewListingLater(done.Host.Name)
        : Localization.ShareDoneNewListing;

    /// <summary>The page in the answer of the host, else its site, because the address of a page is known only to the host.</summary>
    public Uri? ShareDoneLink => _sharePublished is { } done ? done.Page ?? done.Host.Page : null;

    public string? ShareDoneOpenText => _sharePublished is not { } done ? null
        : done.Page is null ? Localization.FormatShareOpenHost(done.Host.Name)
        : Localization.ShareOpenPage;

    public string? ShareDoneTakeDownText => _sharePublished is { } done && !_isShareDoneTakenDown ? Localization.FormatShareTakeDownVersion(done.Version) : null;

    /// <summary>The page in the answer of the host, else the homepage that the index lists for a new version while it is on the host. Borea never builds this address.</summary>
    private Uri? ShareDoneManagePage => _sharePublished is not { } done ? null
        : done.Page ?? (ListedShareAuthored?.Links.TryGetValue("homepage", out var homepage) == true ? done.Host.PageOnHost(homepage) : null);

    public string? ShareDoneManageOnHostText => _sharePublished is { } done && ShareDoneManagePage is not null ? Localization.FormatShareManageOnHost(done.Host.Name) : null;

    /// <summary>A new version sends only the upload and its mods, because the host keeps the listing as it is.</summary>
    public bool IsNewVersionShare => _shareListing is not null;

    /// <summary>The question comes before any field, because the answer decides which fields a share has.</summary>
    public bool IsAskingShareListing => _askedShareListing is not null;

    public bool ShowsShareFields => !IsNewVersionShare && !IsAskingShareListing;

    public string? ShareLooksLikeText => _askedShareListing is { } asked ? Localization.FormatShareLooksLike(asked.Name, ShareHostNameOf(asked)) : null;

    public string? ShareVersionOnlineText => _askedShareListing?.Version is { } version ? Localization.FormatShareVersionOnline(version) : null;

    public string? ShareAsNewVersionText => _askedShareListing is { } asked ? Localization.FormatShareAsNewVersion(asked.Name) : null;

    public string ShareIsDifferentText => IsSave ? Localization.ShareIsDifferentSave : Localization.ShareIsDifferentVehicle;

    public ObservableCollection<ListingStepItem> ShareSteps { get; } = [];

    public string ShareDescribeHeading => IsSave ? Localization.ShareDescribeSave : Localization.ShareDescribeVehicle;

    public string ShareSentenceExample => IsSave ? Localization.ShareSentenceSaveExample : Localization.ShareSentenceVehicleExample;

    public string ShareModsHint => IsSave ? Localization.ShareModsSaveHint : Localization.ShareModsVehicleHint;

    public string? ShareItemText => _share is { } share ? Localization.FormatShareItemIn(share.Content.Name, _owner.InstanceName(share.InstanceId)) : null;

    public string? ShareNewVersionText => _shareListing is { } shared ? Localization.FormatShareNewVersionLine(shared.Name, ShareHostNameOf(shared)) : null;

    private string ShareListingName => _shareListing?.Name ?? Draft.Name;

    private string ShareHostNameOf(ShareListing listing) => _shareHosts.FirstOrDefault(host => host.Key == listing.HostKey)?.Name ?? listing.HostName ?? listing.HostKey;

    public bool ShowsPreviewCard => !IsShareStep || !IsShareChoose;

    /// <summary>What the index holds for the listing of a new version on the steps, or null when it does not hold it yet. The full form previews what the player types, because that draft goes out.</summary>
    private ModMetadata? ListedShareAuthored => IsShareStep && _share is { } share && _shareListing is { } listed
        ? ShareListingsOf(share).FirstOrDefault(listing => ModIds.Equals(listing.Id, listed.Id))?.Authored
        : null;

    public string PreviewHeading => !IsShareStep ? Localization.ListingPreview : IsSave ? Localization.SharePreviewSave : Localization.SharePreviewVehicle;

    public IReadOnlyList<ShareLicenseChoice> ShareLicenseChoices => ShareLicenseChoicesIn(Localization);

    internal static IReadOnlyList<ShareLicenseChoice> ShareLicenseChoicesIn(LocalizationService localization) =>
        [.. ShareLicenses.Select(license => new ShareLicenseChoice(license.Spdx, license.Label(localization))), new ShareLicenseChoice(null, localization.ShareLicenseOther)];

    public bool IsOtherShareLicense => _choseOtherLicense;

    /// <summary>The license in words, or the text that the player typed for another license.</summary>
    public string ShareLicenseText => ShareLicenseChoices.FirstOrDefault(choice => choice.Value == License.Trim())?.Label ?? License.Trim();

    public string ShareSummaryModsText => Dependencies.Where(row => row.Kind == "required").Select(row => row.ModName).ToList() is { Count: > 0 } mods
        ? Localization.FormatShareSummaryMods(string.Join(", ", mods))
        : Localization.ShareSummaryNoMods;

    public string? ShareNeedsBuildText => _share?.Content.GameBuild is { } build
        ? Localization.FormatShareNeedsBuild(GameVersion.TryParse(build, out var version) ? version.ToString() : build)
        : null;

    public ShareLicenseChoice? SelectedShareLicense
    {
        get => IsOtherShareLicense ? ShareLicenseChoices[^1] : ShareLicenseChoices.FirstOrDefault(choice => choice.Value == License.Trim());
        set
        {
            // The box clears its selection while its list changes with the language.
            if (value is null)
                return;

            _choseOtherLicense = value.Value is null;
            if (value.Value is not null)
                License = value.Value;
            else if (IsListedLicense(License))
                License = string.Empty;

            RefreshLicenseChoice();
        }
    }

    internal static bool IsListedLicense(string license) => ShareLicenses.Any(listed => listed.Spdx == license.Trim());

    /// <summary>The name that the index lists for the mod, or the id when it lists none.</summary>
    internal string ShareModName(string id) => (_snapshot is { } snapshot ? ListingDependencyChoices.Listing(snapshot, id)?.Authored?.Name : null) ?? id;

    internal string ShareModVersionText(string version) => Localization.FormatShareModVersion(version);

    internal string ShareModChangedKindText => Localization.ShareModChangedKind;

    private void RefreshLicenseChoice()
    {
        // A license that the list does not name opens the field, which then stays while the player clears or retypes the text.
        if (_share is not null && License.Trim().Length > 0 && !IsListedLicense(License))
            _choseOtherLicense = true;

        OnPropertyChanged(nameof(IsOtherShareLicense));
        OnPropertyChanged(nameof(SelectedShareLicense));
    }

    private IReadOnlyList<ContentIndexListing> ShareListingsOf(GameSaveShareStart share) =>
        (share.Entry.Kind == GameSaveKind.Save ? _snapshot?.Saves : _snapshot?.Vehicles) ?? [];

    /// <summary>
    /// The record of a share comes first, because the index holds a new listing only hours later, and the install record next, because it holds on a second computer.
    /// Then come a listing that installs under the name of the item and a listing with the id. A match is no proof of ownership, and the host decides with not_owner.
    /// </summary>
    private ShareListing? FindShareListing(GameSaveShareStart share, string id)
    {
        var name = share.Content.Name;
        var listings = ShareListingsOf(share);
        if (share.SharedEntry is { } entry && !_declinedShareListings.Contains(entry.ListingId))
        {
            var listed = listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, entry.ListingId));

            // A record names no version once the player took down the last one.
            var recorded = entry.Version.Length > 0 ? entry.Version : null;
            return new ShareListing(entry.ListingId, listed?.Authored?.Name ?? entry.FolderName, entry.HostKey, entry.HostName, entry.ListingId, listed is null ? recorded : NewestShareVersion(listed, recorded), listed is not null && HoldsShareName(listed, name));
        }

        return listings.Where(listing => IsUndeclined(listing, share.InstalledListingId))
            .Concat(listings.Where(listing => HoldsShareName(listing, name)))
            .Concat(listings.Where(listing => IsUndeclined(listing, id)))
            .Select(listing => OnShareHost(listing, name))
            .FirstOrDefault(found => found is not null);

        bool IsUndeclined(ContentIndexListing listing, string? listingId) => ModIds.Equals(listing.Id, listingId) && !_declinedShareListings.Contains(listing.Id);
    }

    private static ShareListing? OnShareHost(ContentIndexListing listing, string name) =>
        listing.Authored?.Releases?.Hosts.FirstOrDefault(host => host.Kind == ReleaseHostKind.Share) is { } host
            ? new ShareListing(listing.Id, listing.Authored?.Name ?? listing.Id, host.Host, null, host.Reference, NewestShareVersion(listing), HoldsShareName(listing, name))
            : null;

    /// <summary>The recorded version counts only while the index does not hold it, because the index holds a new release hours later and may have yanked an older one.</summary>
    private static string? NewestShareVersion(ContentIndexListing listing, string? recorded = null)
    {
        var newest = listing.Releases.Where(release => !release.Yanked).Select(release => (ModVersion?)release.Version).Max();
        return ModVersion.TryParse(recorded, out var waiting) && listing.Releases.All(release => release.Version != waiting) && (newest is null || waiting > newest)
            ? recorded
            : newest?.ToString();
    }

    /// <summary>The release checks refuse the install folder of any release. The name of a listing stays out, because two listings with one name still install apart.</summary>
    private static bool HoldsShareName(ContentIndexListing listing, string name) =>
        listing.Releases.Any(release => string.Equals(release.Install?.Folder, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why the item cannot start as it is named, because a listing that Borea cannot add a version to installs under its name.
    /// A listing on a share host asks its question instead. This compares in Borea and never waits for an answer of a host.
    /// </summary>
    internal string? ShareNameTakenText(GameSaveShareStart share) =>
        FindShareListing(share, ListingShareId.From(share.Content.Name)) is null && ShareListingsOf(share).Any(listing => HoldsShareName(listing, share.Content.Name))
            ? ShareFolderTakenText(share)
            : null;

    private string ShareFolderTakenText(GameSaveShareStart share) => share.Entry.Kind == GameSaveKind.Save
        ? Localization.FormatShareFolderTakenSave(share.Content.Name)
        : Localization.FormatShareFolderTakenVehicle(share.Content.Name);

    /// <summary>A share on the publish step that only now finds its listing goes back, because the question stands on the step before.</summary>
    private void LookUpShareListing(GameSaveShareStart share)
    {
        _askedShareListing = FindShareListing(share, Id.Trim());
        if (IsAskingShareListing && IsSharePublish)
            BackToShareDescribe();

        if (ShareNameTakenText(share) is { } taken)
            RenameShareNameTaken(share, taken, asked: null);
    }

    [RelayCommand]
    private void ShareAsNewVersion()
    {
        if (_askedShareListing is not { } asked)
            return;

        _askedShareListing = null;
        _shareListing = asked;
        Id = asked.Id;
        RefreshShare();
        Refresh();
    }

    /// <summary>The name stays only when no listing holds it, and a short name that is taken opens More options through its error.</summary>
    [RelayCommand]
    private async Task ShareIsDifferentAsync()
    {
        if (_share is not { } share || _askedShareListing is not { } asked)
            return;

        if (asked.HoldsTheName)
            RenameShareNameTaken(share, ShareFolderTakenText(share), asked);
        else
            await GoOnWithoutShareListingAsync(share, asked);
    }

    /// <summary>A Cancel in the rename changes nothing, so the player can still answer the question.</summary>
    private void RenameShareNameTaken(GameSaveShareStart share, string reason, ShareListing? asked) =>
        _owner.BeginShareRename(share, reason, async renamed =>
        {
            if (_share != share)
                return;

            await GoOnWithoutShareListingAsync(TakeShareRename(share, renamed), asked);
        });

    /// <summary>The record of the share goes with the answer, because it would ask the same question at the next share. A rename has moved it to the new name.</summary>
    private async Task GoOnWithoutShareListingAsync(GameSaveShareStart share, ShareListing? declined)
    {
        if (declined is not null)
            _declinedShareListings.Add(declined.Id);

        LookUpShareListing(share);
        RefreshShare();
        Refresh();

        if (share.SharedEntry is { } entry && ModIds.Equals(entry.ListingId, declined?.Id) && _owner.Services is { } services)
        {
            await services.SharedContent.RemoveAsync(share.Entry.Kind.ToContentType(), entry.HostKey, share.Entry.FolderName);
            await _owner.ShowSharedContentAsync(share.Entry.Kind);
        }
    }

    /// <summary>
    /// Decides whether the share can go on. A new listing needs the required fields, and an error that the step has no own text for
    /// counts as one outside the step, because its message is in English and the full form shows it. A new version sends no
    /// fields of the listing, so only its mods can hold it back, and the question holds back everything until it has an answer.
    /// </summary>
    private void RefreshShareGate()
    {
        if (_share is null)
            return;

        HasNoShareMods = Dependencies.Count == 0 && _share.Proposal.Dependencies.Count == 0 && ShareNotes.Count == 0 && !HasShareExtraFiles;

        if (!ShowsShareFields)
        {
            ShareIdError = null;
            ShareSentenceError = null;
            ShareLicenseError = null;
            ShareNextReason = null;
            ShowShareOtherFields(IsNewVersionShare ? Errors.Where(issue => SectionOf(issue) == "dependencies") : []);
            CanContinueShare = IsNewVersionShare && !HasShareOtherFields;
            return;
        }

        var length = Draft.Abstract.EnumerateRunes().Count();
        ShareSentenceCount = Localization.FormatDiscoverCountShown(length, ListingDraft.AbstractLimit.ToString(CultureInfo.CurrentCulture));
        ShareSentenceError = length > ListingDraft.AbstractLimit ? Localization.FormatShareSentenceTooLong(length, ListingDraft.AbstractLimit) : null;
        ShareIdError = ShareIdProblem();
        ShareLicenseError = Draft.License.Length > 0 && Errors.Any(issue => issue.Location == "license") ? Localization.ShareLicenseInvalid : null;

        var missing = new[]
        {
            (Value: Draft.Name, Label: Localization.ShareMissingName, Location: "name"),
            (Value: string.Concat(Draft.Authors), Label: Localization.ShareMissingAuthor, Location: "authors"),
            (Value: Draft.Abstract, Label: Localization.ShareMissingSentence, Location: "abstract"),
            (Value: Draft.License, Label: Localization.ShareMissingLicense, Location: "license"),
            (Value: Draft.Id, Label: Localization.ShareMissingId, Location: "id"),
        }.Where(field => field.Value.Length == 0).ToList();

        var explained = missing.Select(field => field.Location).ToList();
        if (ShareIdError is not null)
            explained.Add("id");
        if (ShareSentenceError is not null)
            explained.Add("abstract");
        if (ShareLicenseError is not null)
            explained.Add("license");
        var names = explained.ToHashSet();
        ShowShareOtherFields(Errors.Where(issue => !IsCovered(issue, explained, names)));

        ShareNextReason = missing.Count > 0
            ? Localization.FormatListingStillMissing(string.Join(", ", missing.Select(field => field.Label)))
            : ShareSentenceError ?? ShareIdError ?? ShareLicenseError;
        CanContinueShare = ShareNextReason is null && !HasShareOtherFields;
    }

    /// <summary>An issue without a label of its own takes the name of its section, and one without either leaves the general text.</summary>
    internal void ShowShareOtherFields(IEnumerable<ListingIssue> issues)
    {
        var fields = issues.Select(issue => LabelOf(issue.Location) ?? Steps.FirstOrDefault(step => step.Key == SectionOf(issue))?.Name ?? string.Empty).Distinct().ToList();
        ShareOtherFieldsText = fields.Count == 0 ? null
            : fields.Contains(string.Empty) ? Localization.ShareOtherFields
            : Localization.FormatShareFieldsToFix(string.Join(", ", fields));
        HasShareOtherFields = fields.Count > 0;
    }

    private string? ShareIdProblem()
    {
        var id = Draft.Id;
        if (id.Length == 0)
            return Localization.ShareIdEmpty;
        if (!ModIds.IsValid(id))
            return Localization.ShareIdInvalid;

        return ModIds.Equals(id, _refusedShareId) || (_snapshot is { } snapshot && (ListingDependencyChoices.IsHeld(snapshot, id) || snapshot.Vehicles.Concat(snapshot.Saves).Any(listing => ModIds.Equals(listing.Id, id))))
            ? Localization.FormatShareIdHeld(id)
            : null;
    }

    partial void OnShareIdErrorChanged(string? value)
    {
        if (value is not null)
            IsShareMoreOptionsOpen = true;
    }

    /// <summary>A field of More options must not stay hidden behind a disabled Next.</summary>
    partial void OnHasShareOtherFieldsChanged(bool value)
    {
        if (value)
            IsShareMoreOptionsOpen = true;
    }

    partial void OnShareFlowStepChanged(ShareFlowStep value)
    {
        RefreshShareSteps();
        RefreshShareSummary();
    }

    /// <summary>Only the steps before can change the summary, so it follows the step and the language.</summary>
    private void RefreshShareSummary()
    {
        OnPropertyChanged(nameof(ShareLicenseText));
        OnPropertyChanged(nameof(ShareSummaryModsText));
        OnPropertyChanged(nameof(ShareNeedsBuildText));
    }

    private void RefreshShareSteps()
    {
        MainViewModel.Arrange(ShareSteps,
        [
            Row("choose", Localization.ShareStepChoose, ShareFlowStep.Choose),
            Row("describe", Localization.ShareStepDescribe, ShareFlowStep.Describe),
            Row("publish", Localization.ListingShareHostPublish, ShareFlowStep.Publish),
            Row("done", Localization.ListingStepDone, ShareFlowStep.Done),
        ]);

        ListingStepItem Row(string key, string name, ShareFlowStep step)
        {
            var state = step < ShareFlowStep || IsShareDone ? ListingStepState.Done : step == ShareFlowStep ? ListingStepState.Current : ListingStepState.ToDo;
            return new ListingStepItem(key, name, state, string.Empty);
        }
    }

    /// <summary>A click on a row of the side panel, which goes back by the same rule as the Back button.</summary>
    internal void OpenShareStep(string key)
    {
        if (IsSendingSharePublish)
            return;

        if (key == "describe" && ShareFlowStep == ShareFlowStep.Publish)
            BackToShareDescribe();
        else if (key == "choose" && ShareFlowStep is ShareFlowStep.Describe or ShareFlowStep.Publish)
            BackToShareChoose();
    }

    [RelayCommand]
    private void ContinueShare()
    {
        if (CanContinueShare)
            ShareFlowStep = ShareFlowStep.Publish;
    }

    [RelayCommand]
    private void BackToShareDescribe()
    {
        ForgetShareHostRun();
        ShareFlowStep = ShareFlowStep.Describe;
    }

    /// <summary>Opens the full form with the same draft. The host path stays on the steps, so an upload is dropped.</summary>
    [RelayCommand]
    private void EditAllShareFields()
    {
        ForgetShareHostRun();
        Step = ListingStep.Form;
        Refresh();
    }

    /// <summary>Goes back from the full form. An id that no longer names the listing makes the share a new listing.</summary>
    [RelayCommand]
    private void BackToShareSteps()
    {
        if (_shareListing is { } listed && !ModIds.Equals(listed.Id, Id.Trim()))
        {
            _shareListing = null;
            RefreshShare();
        }

        ShareFlowStep = ShareFlowStep.Describe;
        Step = ListingStep.Share;
        Refresh();
    }

    [RelayCommand]
    private void ToggleShareMoreOptions() => IsShareMoreOptionsOpen = !IsShareMoreOptionsOpen;

    [RelayCommand]
    private void OpenShareDonePage() => OpenShareDoneLink(ShareDoneLink);

    [RelayCommand]
    private void OpenSharePullRequest() => OpenShareDoneLink(SharePullRequest);

    private void OpenShareDoneLink(Uri? link)
    {
        if (link is null)
            return;

        ShareHostError = null;
        OpenShareHostPage(link.AbsoluteUri);
    }

    [RelayCommand]
    private Task CopyShareDoneLinkAsync() => _owner.CopyShareLinkAsync(ShareDoneLink?.AbsoluteUri);

    [RelayCommand]
    private void ChangeShareDoneOnHost()
    {
        if (ShareDoneManageTarget([]) is { } target)
            _owner.BeginShareManage(target, ShareManageAction.Change);
    }

    [RelayCommand]
    private void TakeDownShareDone()
    {
        if (_sharePublished is { } done && !_isShareDoneTakenDown && ShareDoneManageTarget([done.Version]) is { } target)
            _owner.BeginShareManage(target, ShareManageAction.TakeDown);
    }

    [RelayCommand]
    private void OpenShareDoneManagePage() => OpenShareDoneLink(ShareDoneManagePage);

    /// <summary>A new listing sent the draft, and a new version sent no text, so its fields start from the index, or empty while the index does not hold it.</summary>
    private ShareManageTarget? ShareDoneManageTarget(IReadOnlyList<string> versions)
    {
        if (_sharePublished is not { } done || _share is not { } share)
            return null;

        var fields = !done.IsNewVersion ? new ShareManageFields(Draft.Name, Draft.Abstract, Draft.Description, Draft.License, Draft.Tags)
            : ListedShareAuthored is { } listed ? new ShareManageFields(listed.Name, listed.Abstract, listed.Description, listed.License, listed.Tags)
            : new ShareManageFields(done.Name, string.Empty, null, string.Empty, []);
        return new ShareManageTarget(done.Host, share.Entry.Kind.ToContentType(), done.Id, done.Name, ShareDoneManagePage, fields, versions);
    }

    /// <summary>The published version of the step is gone, so the step no longer says that everyone can download it.</summary>
    internal void ShowShareTakenDown(string listingId, string version)
    {
        if (_sharePublished is not { } done || !ModIds.Equals(done.Id, listingId) || done.Version != version)
            return;

        _isShareDoneTakenDown = true;
        RefreshShareRun();
    }
}
