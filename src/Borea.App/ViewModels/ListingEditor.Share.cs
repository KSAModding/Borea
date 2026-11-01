using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.Sharing;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>A vehicle or a save that the player shares from the instance page, with what Borea read from it.</summary>
/// <param name="LastAuthor">The authors and license of the player's last new listing, which a share starts with, or null.</param>
internal sealed record GameSaveShareStart(Guid InstanceId, GameSaveEntry Entry, GameFolderContent Content, ContentShare Proposal, SharedContentAuthor? LastAuthor);

/// <summary>
/// A new vehicle or save listing (RFC 0085): the editor starts with the dependencies Borea proposes, shows what it could not map,
/// and saves the archive of the folder. Until a share host exists, the player attaches the archive to a release of their own.
/// </summary>
public sealed partial class ListingEditor
{
    private GameSaveShareStart? _share;
    private (string File, GameSaveArchive Archive)? _savedArchive;

    public bool IsVehicleOrSave => Type is ListingFolders.VehicleType or ListingFolders.SaveType;

    public bool HasShare => _share is not null;

    public string ForumsHint => IsVehicleOrSave ? Localization.ListingForumsOptionalHint : Localization.ListingForumsHint;

    public string GameHint => IsVehicleOrSave ? Localization.ListingGameMinFromArchiveHint : Localization.ListingGameHint;

    public bool IsVehicle => Type == ListingFolders.VehicleType;

    public bool IsSave => Type == ListingFolders.SaveType;

    public string IntroText => IsShareStep ? Localization.ShareFlowIntro : IsVehicleOrSave ? Localization.ListingShareIntro : Localization.ListingIntro;

    public string IdHint => IsVehicleOrSave ? Localization.ListingShareIdHint : Localization.ListingIdHint;

    public string GameMaxHint => IsVehicleOrSave ? Localization.ListingShareGameMaxHint : Localization.ListingGameMaxHint;

    public string DependenciesHint => IsVehicleOrSave ? Localization.ListingShareDependenciesHint : Localization.ListingDependenciesHint;

    /// <summary>What Borea could not map to one loaded mod with a valid id, one line each.</summary>
    public ObservableCollection<string> ShareNotes { get; } = [];

    public IReadOnlyList<string> ShareExtraFiles => _share?.Content.ExtraFiles ?? [];

    public bool HasShareExtraFiles => ShareExtraFiles.Count > 0;

    public string? ShareArchiveText => _savedArchive is { } saved
        ? Localization.FormatListingShareArchiveSaved(saved.File, saved.Archive.Sha256, MainViewModel.SizeText(saved.Archive.Size))
        : null;

    /// <summary>Opens the share steps on a new document of the type, named after meta.toml, with the proposed dependencies. It carries no game_min, which the index takes from the archive.</summary>
    internal void StartShare(GameSaveShareStart share)
    {
        StartOver();
        _source = null;
        _archive = null;
        ArchiveText = null;
        Load(new ListingDraft
        {
            Type = share.Entry.Kind == GameSaveKind.Vehicle ? ListingFolders.VehicleType : ListingFolders.SaveType,
            Id = ListingShareId.From(share.Content.Name),
            Name = share.Content.Name,
            Authors = share.LastAuthor?.Authors ?? [],
            License = share.LastAuthor?.License ?? string.Empty,
            Dependencies = share.Proposal.Dependencies,
        });
        _share = share;
        _shareListing = FindShareListing();
        ShareFlowStep = ShareFlowStep.Describe;
        Step = ListingStep.Share;
        RefreshShare();
        StartShareHosts();
        Refresh();
    }

    private void ForgetShare()
    {
        _share = null;
        _shareListing = null;
        _refusedShareId = null;
        _savedArchive = null;
        _choseOtherLicense = false;
        HasFormBehindShareChoose = false;
        _shareHosts = [];
        _hasReadShareHosts = false;
        ShareHostStatus = "stable";
        ShareIdError = null;
        IsShareMoreOptionsOpen = false;
        ForgetShareHostRun();
        RefreshShare();
    }

    private void RefreshShare()
    {
        MainViewModel.Arrange(ShareNotes, _share?.Proposal.Notes.Select(NoteText).ToList() ?? []);
        OnPropertyChanged(nameof(HasShare));
        OnPropertyChanged(nameof(ShareExtraFiles));
        OnPropertyChanged(nameof(HasShareExtraFiles));
        OnPropertyChanged(nameof(ShareArchiveText));
        OnPropertyChanged(nameof(ShareStatusChoices));
        OnPropertyChanged(nameof(SelectedShareStatus));
        OnPropertyChanged(nameof(IsNewVersionShare));
        OnPropertyChanged(nameof(ShareDescribeHeading));
        OnPropertyChanged(nameof(ShareSentenceExample));
        OnPropertyChanged(nameof(ShareModsHint));
        OnPropertyChanged(nameof(ShareItemText));
        OnPropertyChanged(nameof(ShareNewVersionText));
        OnPropertyChanged(nameof(ShareLicenseChoices));
        RefreshLicenseChoice();
        RefreshShareSteps();
        RefreshShareSummary();
        RefreshShareHostRows();
    }

    [RelayCommand]
    private async Task SaveShareArchiveAsync()
    {
        if (_share is not { } share)
            return;

        if (await _owner.SaveShareArchiveAsync(share) is { } saved && _share == share)
        {
            _savedArchive = saved;
            OnPropertyChanged(nameof(ShareArchiveText));
        }
    }

    private string NoteText(ContentShareNote note) => note.Kind switch
    {
        ContentShareNoteKind.NotProvided => Localization.FormatListingShareNotProvided(IdsText(note.Ids)),
        ContentShareNoteKind.DisabledProvider => Localization.FormatListingShareDisabledProvider(note.Mods[0], IdsText(note.Ids)),
        ContentShareNoteKind.InvalidFolder => Localization.FormatListingShareInvalidFolder(note.Mods[0], IdsText(note.Ids)),
        ContentShareNoteKind.SharedId => Localization.FormatListingShareSharedId(IdsText(note.Ids), string.Join(", ", note.Mods)),
        _ => Localization.FormatListingShareUnreadableFile(_share is { } share && note.Path is { } path ? _owner.ShortSharePath(share.InstanceId, path) : string.Empty),
    };

    private string IdsText(IEnumerable<ContentId> ids) => string.Join(", ", ids.Select(id => Localization.FormatListingShareContentId(KindName(id.Kind), id.Id)));

    private string KindName(ContentIdKind kind) => kind switch
    {
        ContentIdKind.Part => Localization.ListingShareKindPart,
        ContentIdKind.SubstancePhase => Localization.ListingShareKindSubstance,
        ContentIdKind.Character => Localization.ListingShareKindCharacter,
        ContentIdKind.Body => Localization.ListingShareKindBody,
        _ => Localization.ListingShareKindSystem,
    };
}
