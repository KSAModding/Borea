using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Borea.App.Localization;
using Borea.Core.Game;
using Borea.Core.Index;
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

    /// <summary>The listing of the shared item on a share host, which makes the share a new version of it, or null for a new listing.</summary>
    private (ContentIndexListing Listing, ReleaseHost Entry)? _shareListing;

    /// <summary>The short name that the host refused at a publish, which the index can hold back only hours later.</summary>
    private string? _refusedShareId;

    private bool _choseOtherLicense;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShareDescribe), nameof(IsSharePublish))]
    private ShareFlowStep _shareFlowStep;

    [ObservableProperty]
    private bool _canContinueShare;

    /// <summary>Why Next is disabled, except for an error outside the step, which <see cref="HasShareOtherFields"/> says.</summary>
    [ObservableProperty]
    private string? _shareNextReason;

    [ObservableProperty]
    private bool _hasShareOtherFields;

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

    public bool IsShareDescribe => ShareFlowStep == ShareFlowStep.Describe;

    public bool IsSharePublish => ShareFlowStep == ShareFlowStep.Publish;

    /// <summary>A new version sends only the upload and its mods, because the host keeps the listing as it is.</summary>
    public bool IsNewVersionShare => _shareListing is not null;

    public ObservableCollection<ListingStepItem> ShareSteps { get; } = [];

    public string ShareDescribeHeading => IsSave ? Localization.ShareDescribeSave : Localization.ShareDescribeVehicle;

    public string ShareSentenceExample => IsSave ? Localization.ShareSentenceSaveExample : Localization.ShareSentenceVehicleExample;

    public string ShareModsHint => IsSave ? Localization.ShareModsSaveHint : Localization.ShareModsVehicleHint;

    public string? ShareItemText => _share is { } share ? Localization.FormatShareItemIn(share.Content.Name, _owner.InstanceName(share.InstanceId)) : null;

    public string? ShareNewVersionText => _shareListing is { } shared
        ? Localization.FormatShareNewVersionLine(ShareListingName, _shareHosts.FirstOrDefault(host => host.Key == shared.Entry.Host)?.Name ?? shared.Entry.Host)
        : null;

    private string ShareListingName => _shareListing is { } shared ? shared.Listing.Authored?.Name ?? shared.Listing.Id : Draft.Name;

    public bool ShowsPreviewCard => !IsShareStep || !IsNewVersionShare;

    public string PreviewHeading => !IsShareStep ? Localization.ListingPreview : IsSave ? Localization.SharePreviewSave : Localization.SharePreviewVehicle;

    public IReadOnlyList<ShareLicenseChoice> ShareLicenseChoices =>
        [.. ShareLicenses.Select(license => new ShareLicenseChoice(license.Spdx, license.Label(Localization))), new ShareLicenseChoice(null, Localization.ShareLicenseOther)];

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

    private static bool IsListedLicense(string license) => ShareLicenses.Any(listed => listed.Spdx == license.Trim());

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

    private (ContentIndexListing Listing, ReleaseHost Entry)? FindShareListing()
    {
        var listings = Type == ListingFolders.SaveType ? _snapshot?.Saves : _snapshot?.Vehicles;
        var found = listings?.FirstOrDefault(listing => ModIds.Equals(listing.Id, Id.Trim()));
        return found?.Authored?.Releases?.Hosts.FirstOrDefault(entry => entry.Kind == ReleaseHostKind.Share) is { } host ? (found, host) : null;
    }

    /// <summary>
    /// Decides whether the share can go on. A new listing needs the required fields, and an error that the step has no own text for
    /// counts as one outside the step, because its message is in English and the full form shows it. A new version sends no
    /// fields of the listing, so only its mods can hold it back.
    /// </summary>
    private void RefreshShareGate()
    {
        if (_share is null)
            return;

        HasNoShareMods = Dependencies.Count == 0 && _share.Proposal.Dependencies.Count == 0 && ShareNotes.Count == 0 && !HasShareExtraFiles;

        if (IsNewVersionShare)
        {
            ShareIdError = null;
            ShareSentenceError = null;
            ShareLicenseError = null;
            ShareNextReason = null;
            HasShareOtherFields = Errors.Any(issue => SectionOf(issue) == "dependencies");
            CanContinueShare = !HasShareOtherFields;
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
        HasShareOtherFields = Errors.Any(issue => !IsCovered(issue, explained, names));

        ShareNextReason = missing.Count > 0
            ? Localization.FormatListingStillMissing(string.Join(", ", missing.Select(field => field.Label)))
            : ShareSentenceError ?? ShareIdError ?? ShareLicenseError;
        CanContinueShare = ShareNextReason is null && !HasShareOtherFields;
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
            new ListingStepItem("choose", Localization.ShareStepChoose, ListingStepState.Done, string.Empty),
            Row("describe", Localization.ShareStepDescribe, ShareFlowStep.Describe),
            Row("publish", Localization.ListingShareHostPublish, ShareFlowStep.Publish),
            Row("done", Localization.ListingStepDone, ShareFlowStep.Done),
        ]);

        ListingStepItem Row(string key, string name, ShareFlowStep step)
        {
            var state = step < ShareFlowStep ? ListingStepState.Done : step == ShareFlowStep ? ListingStepState.Current : ListingStepState.ToDo;
            return new ListingStepItem(key, name, state, string.Empty);
        }
    }

    /// <summary>A click on a row of the side panel, which goes back by the same rule as the Back button.</summary>
    internal void OpenShareStep(string key)
    {
        if (key == "describe" && ShareFlowStep == ShareFlowStep.Publish && !IsSendingSharePublish)
            BackToShareDescribe();
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
        if (_shareListing is { } listed && !ModIds.Equals(listed.Listing.Id, Id.Trim()))
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
}
