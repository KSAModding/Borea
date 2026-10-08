using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.ModLoaders;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// A pack version: its own version, and the members it pins. Members come only from the snapshot, so a new pin
/// always names a listed mod at a release that no client refuses to install.
/// </summary>
public sealed partial class ListingEditor
{
    private IReadOnlyList<ContentIndexListing> _memberCandidates = [];
    private (string InstanceName, IReadOnlyList<ListingLeftOutMod> Mods)? _fromInstance;

    /// <summary>The required dependencies that no member pins, which "Add the missing dependencies" pins.</summary>
    private ListingPackFix _missing = new([], []);

    [ObservableProperty]
    private string _packVersion = string.Empty;

    [ObservableProperty]
    private string _releasedAt = string.Empty;

    [ObservableProperty]
    private string _changelog = string.Empty;

    public ObservableCollection<ListingPackMemberRow> Members { get; } = [];

    [ObservableProperty]
    private string _memberQuery = string.Empty;

    /// <summary>The listed mods that are not pinned yet and whose id, name or authors contain the query.</summary>
    public ObservableCollection<ListedListing> MemberMatches { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    private ListedListing? _selectedMemberMatch;

    public bool HasNoMemberMatch => MemberMatches.Count == 0 && _memberCandidates.Count > 0;

    /// <summary>The game_min that the pinned releases need, while the form has a lower or no game_min.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameMinProposalText))]
    private string? _gameMinProposal;

    public string? GameMinProposalText => GameMinProposal is { } gameMin ? Localization.FormatListingGameMinProposal(gameMin) : null;

    /// <summary>The mods of the instance a new pack was made from that are not members, each with its reason.</summary>
    public ObservableCollection<string> LeftOut { get; } = [];

    public bool HasLeftOut => LeftOut.Count > 0;

    public string? LeftOutTitle => _fromInstance is { } from ? Localization.FormatListingLeftOut(from.InstanceName) : null;

    /// <summary>The first version of a new pack claims its id, so its pull request also adds the owner record.</summary>
    public bool IsNewPack => IsPack && !IsNextVersion;

    public string OwnerFilePath => ListingPackOwner.PathOf(Draft.Id);

    /// <summary>
    /// The owner record for the browser path. Signed in, the ownership check of a free id gives the record of the account.
    /// Otherwise Borea does not know the numeric account id, so the author puts it in, and the checks refuse the record
    /// until it names the account that opens the pull request.
    /// </summary>
    public string OwnerFile => (Ownership?.Claim ?? new ListingPackOwner(_owner.GitHubLogin ?? "your-github-login", 0)).ToJson();

    public string OwnerFileText => Localization.FormatListingOwnerFileText(OwnerFilePath);

    [RelayCommand]
    private void StartPack() => StartNewPack(NewPack());

    [RelayCommand]
    private async Task CopyOwnerFileAsync()
    {
        if (_owner.WindowServices is not { } window)
            return;

        await window.CopyTextAsync(OwnerFile);
        _owner.ShowSuccessToast(() => Localization.ListingOwnerFileCopied);
    }

    [RelayCommand]
    private async Task SaveOwnerFileAsync()
    {
        if (_owner.WindowServices is not { } window)
            return;

        try
        {
            var fileName = await window.SaveTextFileAsync(Localization.ListingSaveOwnerFile, ListingPackOwner.FileName, Localization.ListingOwnerFileType, OwnerFile);
            if (fileName is not null)
                _owner.ShowSuccessToast(() => Localization.FormatListingSaved(fileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _owner.ShowErrorToast(() => Localization.ListingSaveFailed, exception.Message);
        }
    }

    /// <summary>
    /// Opens a new pack of the enabled mods that Borea installed from the index in an instance, at their installed versions,
    /// named after the instance. Returns false, and opens nothing, when there is no snapshot to tell which mods a pack can pin.
    /// </summary>
    internal bool StartPackFromInstance(Instance instance, IReadOnlyList<ModManifestEntry> manifest, IReadOnlyDictionary<string, LoaderInstallation> loaderInstallations)
    {
        StartOver();
        if (_snapshot is not { } snapshot)
            return false;

        var pack = ListingPackFromInstance.Of(snapshot, instance, manifest, loaderInstallations);
        StartNewPack(
            NewPack() with
            {
                Name = instance.Name,
                GameMin = ListingPackMembers.HighestGameMin(snapshot, pack.Members)?.GameMin ?? string.Empty,
                Mods = pack.Members,
            },
            (instance.Name, pack.LeftOut));
        return true;
    }

    private static ListingDraft NewPack() => new()
    {
        Type = ListingDraft.ModPackType,
        Version = "1.0.0",
        ReleasedAt = Timestamp(DateTimeOffset.UtcNow),
    };

    private void StartNewPack(ListingDraft draft, (string InstanceName, IReadOnlyList<ListingLeftOutMod> Mods)? fromInstance = null)
    {
        _source = null;
        _archive = null;
        ArchiveText = null;
        Load(draft, fromInstance: fromInstance);
    }

    /// <summary>Opens the next version of a listed pack, as the pack page asks for it.</summary>
    internal Task MakeNextVersionAsync(string packId)
    {
        StartOver();
        return LoadListedCoreAsync(packId, isPack: true);
    }

    /// <summary>Pins the chosen mod at its newest stable release that a pack can pin, by the order of <see cref="ListingPackDependencies.DefaultRelease"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAddMember))]
    private void AddMember()
    {
        if (SelectedMemberMatch?.Id is not { } id || _snapshot is not { } snapshot || ListingPackMembers.Listing(snapshot, id) is not { } listing
            || ListingPackDependencies.DefaultRelease(snapshot, listing) is not { } release)
            return;

        Members.Add(MemberRow(new ListingPackMember(listing.Id, release.Version.ToString())));
        FindMembers();
        Refresh();
    }

    private bool CanAddMember() => SelectedMemberMatch is not null;

    public bool CanAddMissingDependencies => _missing.Pins.Count > 0;

    /// <summary>What "Add the missing dependencies" leaves to the author, such as a member that needs one of several mods.</summary>
    public ObservableCollection<string> MissingDependencyNotes { get; } = [];

    public bool HasMissingDependencyNotes => MissingDependencyNotes.Count > 0;

    [RelayCommand]
    private void AddMissingDependencies()
    {
        if (_missing.Pins.Count == 0)
            return;

        foreach (var pin in _missing.Pins)
            Members.Add(MemberRow(pin));
        FindMembers();
        Refresh();
    }

    private void FillMissing(IReadOnlyList<ListingPackMember> mods)
    {
        _missing = IsPack && _snapshot is { } snapshot ? ListingPackDependencies.Missing(snapshot, mods) : new ListingPackFix([], []);
        MainViewModel.Arrange(MissingDependencyNotes, _missing.Needs.Select(need => need.Reason switch
        {
            ListingPackNeedReason.Choose => Localization.FormatListingMissingChoose(need.Text),
            ListingPackNeedReason.NotListed => Localization.FormatListingMissingNotListed(need.Text),
            _ => Localization.FormatListingMissingNoRelease(need.Text),
        }).ToList());
        OnPropertyChanged(nameof(CanAddMissingDependencies));
        OnPropertyChanged(nameof(HasMissingDependencyNotes));
    }

    [RelayCommand]
    private void UseGameMinProposal()
    {
        if (GameMinProposal is { } gameMin)
            GameMin = gameMin;
    }

    /// <summary>Copies the lines that forum rule 4.3 asks a pack thread for, for the members of the draft.</summary>
    [RelayCommand]
    private Task CopyForumListAsync()
    {
        if (_owner.Services is not { } services)
            return Task.CompletedTask;

        var pins = Members.Select(row => row.ToMember())
            .Where(member => ModIds.IsValid(member.Id) && ModVersion.TryParse(member.Version, out _))
            .Select(member => new ModPackEntry(member.Id, ModVersion.Parse(member.Version)))
            .ToList();
        return _owner.CopyTextAsync(
            async () => string.Join(Environment.NewLine, await ModPackForumList.WriteAsync(pins, services.ContentIndex)),
            () => Localization.PackForumListName,
            () => Localization.PackForumListCopied);
    }

    internal void Remove(ListingPackMemberRow row)
    {
        if (!Members.Remove(row))
            return;

        FindMembers();
        Refresh();
    }

    /// <summary>
    /// The note for a pin whose download is gone, which the member rules accept. A pin they refuse gets their error instead.
    /// </summary>
    internal string? MemberNote(ListingPackMember member)
    {
        if (_snapshot is not { } snapshot || ListingPackMembers.Release(snapshot, member) is not null)
            return null;

        return ListingPackMembers.Pinned(snapshot, member)?.Download.UnavailableSince is { } since
            ? Localization.FormatListingMemberGone(member.Id, member.Version, MainViewModel.DateText(since))
            : null;
    }

    /// <summary>The newer release in the list of a row, by the "Newer releases" rule of RFC 0080, or null.</summary>
    internal ListingReleaseChoice? NewerChoice(string id, string pinned, IReadOnlyList<ListingReleaseChoice> releases)
    {
        if (_snapshot is not { } snapshot || !ModVersion.TryParse(pinned, out var version)
            || ModPackMemberReleases.NewerRelease(snapshot, id, version) is not { } newer)
            return null;

        var text = newer.Version.ToString();
        return releases.FirstOrDefault(release => release.Version == text);
    }

    /// <summary>Whether the member rules accept the pin of a row at another release, with the pins of the other rows as they are.</summary>
    internal bool NewerFits(ListingPackMemberRow row, string version)
    {
        if (_snapshot is not { } snapshot)
            return false;

        var pin = new ListingPackMember(row.Id, version);
        var pins = Members.Where(other => !ReferenceEquals(other, row)).Select(other => other.ToMember()).Append(pin).ToList();
        return ListingPackDependencies.Fits(snapshot, pins, pin);
    }

    /// <summary>The note of a row whose mod the index marks as disputed, or null.</summary>
    internal string? DisputedNote(string id) =>
        _snapshot is { } snapshot && ListingPackMembers.Listing(snapshot, id) is { IndexStatus: { State: IndexStatusState.Disputed } status }
            ? Localization.FormatListingMemberDisputed(id, status.Reason)
            : null;

    /// <summary>Gives each row the errors whose location is its pin.</summary>
    private void UpdateMemberProblems(IReadOnlyList<ListingIssue> errors)
    {
        for (var index = 0; index < Members.Count; index++)
        {
            var where = $"mods[{index}]";
            Members[index].Update(errors
                .Where(issue => issue.Location == where || issue.Location.StartsWith(where + ".", StringComparison.Ordinal))
                .Select(issue => issue.Message)
                .ToList());
        }
    }

    /// <summary>
    /// The issues with those about pins sorted by section and index, in the places those issues held, so the errors of one
    /// member stand together and the members follow their order. The sort keeps the order of the issues of one pin.
    /// </summary>
    internal static List<ListingIssue> InPinOrder(IEnumerable<ListingIssue> issues)
    {
        var list = issues.ToList();
        var places = list.Select((issue, place) => (Place: place, Key: PinKey(issue.Location))).Where(entry => entry.Key is not null).ToList();
        var sorted = places.OrderBy(entry => entry.Key!.Value).Select(entry => list[entry.Place]).ToList();
        for (var index = 0; index < places.Count; index++)
            list[places[index].Place] = sorted[index];
        return list;
    }

    private static (int Section, int Index)? PinKey(string location) =>
        PinLocation().Match(location) is { Success: true } match
            ? (Array.IndexOf(PinSections, match.Groups[1].Value), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;

    private static readonly string[] PinSections = ["mods", "vehicles", "saves"];

    [GeneratedRegex(@"^(mods|vehicles|saves)\[(\d+)\]")]
    private static partial Regex PinLocation();

    internal string NewerText(string version) => Localization.FormatPackMemberNewer(version);

    internal string UseNewerText(string version) => Localization.FormatListingMemberUseNewer(version);

    /// <summary>What the retractions of the listed pack say about a mod whose id their reasons name.</summary>
    internal IReadOnlyList<string> RetractionNotes(string id) => Retracted()
        .Where(version => ListingPackVersions.Names(version.IndexStatus!.Reason, id))
        .Select(version => Localization.FormatListingMemberNamedInRetraction(version.Metadata.Version.ToString(), id, version.IndexStatus!.Reason!))
        .ToList();

    /// <summary>The pack whose next version the draft is, as the snapshot lists it.</summary>
    private ContentIndexPack? ListedPack() => IsNextVersion && _snapshot is { } snapshot ? ListingPackVersions.Pack(snapshot, _base.Id) : null;

    /// <summary>The retracted versions of the listed pack, newest first.</summary>
    private IEnumerable<ContentIndexPackVersion> Retracted() => (ListedPack()?.Versions ?? [])
        .Where(version => version.IndexStatus?.State == IndexStatusState.Retracted)
        .OrderByDescending(version => version.Metadata.Version);

    /// <summary>
    /// A note for each pin whose download is gone, and the game_min the pins need when the form has a lower one.
    /// For the next version of a listed pack also each retracted version with its reason, each pin that a reason names,
    /// and an error for a version or a release time that does not come after the listed versions.
    /// </summary>
    private List<ListingIssue> PackIssues(IReadOnlyList<ListingPackMember> mods)
    {
        var issues = new List<ListingIssue>();
        for (var index = 0; index < mods.Count; index++)
        {
            if (MemberNote(mods[index]) is { } note)
                issues.Add(new ListingIssue(ListingIssueSeverity.Note, $"mods[{index}]", note));
            foreach (var retraction in RetractionNotes(mods[index].Id))
                issues.Add(new ListingIssue(ListingIssueSeverity.Note, $"mods[{index}]", retraction));
        }

        foreach (var version in Retracted())
            issues.Add(new ListingIssue(ListingIssueSeverity.Note, "version", Localization.FormatListingPackRetracted(version.Metadata.Version.ToString(), version.IndexStatus!.Reason)));
        if (ListedPack() is { } pack)
            issues.AddRange(OrderIssues(pack));
        FillLeftOut(mods);
        FillMissing(mods);

        var needed = _snapshot is { } snapshot ? ListingPackMembers.HighestGameMin(snapshot, mods) : null;
        GameMinProposal = needed is not null && !(GameVersion.TryParse(GameMin.Trim(), out var gameMin) && gameMin.Revision >= needed.GameMinRevision)
            ? needed.GameMin
            : null;
        if (GameMinProposalText is { } proposal)
            issues.Add(new ListingIssue(ListingIssueSeverity.Note, "compatibility", proposal));
        return issues;
    }

    /// <summary>A version that is not above every listed version, and a release time that is not after the newest listed one.</summary>
    private IEnumerable<ListingIssue> OrderIssues(ContentIndexPack pack)
    {
        if (ModVersion.TryParse(PackVersion.Trim(), out var version) && ListingPackVersions.Highest(pack) is { } highest && version <= highest.Metadata.Version)
            yield return new ListingIssue(ListingIssueSeverity.Error, "version", Localization.FormatListingPackVersionNotHigher(PackVersion.Trim(), highest.Metadata.Version.ToString()));

        if (DateTimeOffset.TryParse(ReleasedAt.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var releasedAt)
            && pack.Versions.OrderByDescending(listed => listed.Metadata.ReleasedAt).ThenByDescending(listed => listed.Metadata.Version).FirstOrDefault() is { } newest
            && releasedAt <= newest.Metadata.ReleasedAt)
        {
            yield return new ListingIssue(ListingIssueSeverity.Error, "released_at", Localization.FormatListingPackReleasedAtNotLater(
                ReleasedAt.Trim(), Timestamp(newest.Metadata.ReleasedAt), newest.Metadata.Version.ToString()));
        }
    }

    /// <summary>
    /// The newest version file of a listed pack as its next version: the version raised above every listed version and every
    /// file that main already has, released now, and without the changelog of the version before. When main has a newer file
    /// than the snapshot knows, that file is the newest version, so the draft starts from it.
    /// </summary>
    private async Task<(ListingDraft Draft, string Text, string? Message)> ReadNextVersionAsync(BoreaServices services, ContentIndexPack pack, CancellationToken cancellationToken)
    {
        var highest = ListingPackVersions.Highest(pack)!;
        var loaded = ListingPackVersions.FileVersion(highest);
        var proposed = ListingPackVersions.Raise(highest.Metadata.Version);
        string? message = null;
        string? taken = null;
        try
        {
            if (proposed is { } first
                && await ListingPackVersions.FreeAsync(first, (version, token) => ExistsAsync(version.ToString(), token), cancellationToken) is { } free
                && free != first)
            {
                message = Localization.FormatListingPackVersionTaken(first.ToString(), free.ToString());
                proposed = free;
                loaded = taken!;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Opening the pull request reads main again.
        }

        var text = await services.ListedDocuments.GetPackVersionAsync(pack.Id, loaded, cancellationToken);
        var draft = ListingDraft.FromDocument(services.ListingFormat.Read(text)) with
        {
            Type = ListingDraft.ModPackType,
            ReleasedAt = Timestamp(DateTimeOffset.UtcNow),
            Changelog = null,
        };
        return (proposed is { } next ? draft with { Version = next.ToString() } : draft, text, message);

        async Task<bool> ExistsAsync(string version, CancellationToken token)
        {
            var exists = await services.ListedDocuments.HasPackVersionAsync(pack.Id, version, token);
            if (exists)
                taken = version;
            return exists;
        }
    }

    /// <summary>
    /// Raises the version while main already has a file of it, because the snapshot follows main with a delay.
    /// Returns whether the pull request can open, and what the author reads: the raised version, or why it cannot open.
    /// </summary>
    private async Task<(bool Free, string? Message)> FreeVersionAsync()
    {
        var typed = PackVersion.Trim();
        if (_owner.Services is not { } services || !ModVersion.TryParse(typed, out var proposed))
            return (true, null);

        // The file name keeps build metadata, so the typed version is the first path to read, and the raised ones follow.
        var id = Draft.Id;
        try
        {
            if (!await services.ListedDocuments.HasPackVersionAsync(id, typed))
                return (true, null);
            if (ListingPackVersions.Raise(proposed) is not { } next
                || await ListingPackVersions.FreeAsync(next, (version, token) => services.ListedDocuments.HasPackVersionAsync(id, version.ToString(), token)) is not { } free)
                return (false, Localization.FormatListingPackNoFreeVersion(typed));

            PackVersion = free.ToString();
            return (true, Localization.FormatListingPackVersionTaken(typed, free.ToString()));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return (false, Localization.FormatListingPackCheckFailed(typed, exception.Message));
        }
    }

    /// <summary>The left-out mods of the instance, without those the author has added as members since.</summary>
    private void FillLeftOut(IReadOnlyList<ListingPackMember> mods)
    {
        var lines = (_fromInstance?.Mods ?? [])
            .Where(mod => !mods.Any(member => ModIds.Equals(member.Id, mod.Id)))
            .Select(LeftOutText)
            .ToList();
        MainViewModel.Arrange(LeftOut, lines);
        OnPropertyChanged(nameof(HasLeftOut));
        OnPropertyChanged(nameof(LeftOutTitle));
    }

    private string LeftOutText(ListingLeftOutMod mod) => mod.Reason switch
    {
        ListingLeftOutReason.NotInstalledByBorea => Localization.FormatListingLeftOutNotInstalledByBorea(mod.Id),
        ListingLeftOutReason.ModLoader => mod.Version is { } version
            ? Localization.FormatListingLeftOutModLoader(mod.Id, version)
            : Localization.FormatListingLeftOutModLoaderUnknownVersion(mod.Id),
        ListingLeftOutReason.Disabled => Localization.FormatListingLeftOutDisabled(mod.Id, mod.Version ?? string.Empty),
        ListingLeftOutReason.NotListed => Localization.FormatListingLeftOutNotListed(mod.Id, mod.Version ?? string.Empty),
        ListingLeftOutReason.Yanked => Localization.FormatListingLeftOutYanked(mod.Id, mod.Version ?? string.Empty),
        ListingLeftOutReason.DownloadGone => Localization.FormatListingLeftOutDownloadGone(mod.Id, mod.Version ?? string.Empty, mod.GoneSince is { } since ? MainViewModel.DateText(since) : string.Empty),
        _ => Localization.FormatListingLeftOutReleaseNotListed(mod.Id, mod.Version ?? string.Empty),
    };

    private static string Timestamp(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Builds the member rows again, for a new draft, a new snapshot or a new display language.</summary>
    private void FillMembers(IReadOnlyList<ListingPackMember> members)
    {
        Members.Clear();
        foreach (var member in members)
            Members.Add(MemberRow(member));

        _memberCandidates = _snapshot is { } snapshot ? ListingPackMembers.Candidates(snapshot) : [];
        FindMembers();
    }

    /// <summary>
    /// A row whose release list holds the releases a pin can name, and first the pinned one when it is not among them.
    /// A release that requires a mod that is not listed is marked, because no pack can pin it with a complete set.
    /// </summary>
    private ListingPackMemberRow MemberRow(ListingPackMember member)
    {
        var listing = _snapshot is { } snapshot ? ListingPackMembers.Listing(snapshot, member.Id) : null;
        var releases = (listing is null ? [] : ListingPackMembers.Offered(listing))
            .Select(release => new ListingReleaseChoice(release.Version.ToString(), _owner.ReleaseStatusText(release.ReleaseStatus)) { Mark = Mark(release) })
            .ToList();
        if (!releases.Any(release => release.Version == member.Version))
            releases.Insert(0, new ListingReleaseChoice(member.Version, string.Empty));

        return new ListingPackMemberRow(this, member, listing?.Authored?.Name ?? member.Id, releases);
    }

    private string? Mark(ModVersionMetadata release) =>
        _snapshot is { } snapshot && ListingPackDependencies.UnlistedNeeds(snapshot, release) is { Count: > 0 } needs
            ? Localization.FormatListingMemberCannotBePinned(needs)
            : null;

    private void FindMembers()
    {
        var query = MemberQuery.Trim();
        var matches = _memberCandidates
            .Where(listing => !Members.Any(row => ModIds.Equals(row.Id, listing.Id)) && Matches(listing, query))
            .Select(listing => new ListedListing(listing.Id, listing.Authored!.Name, AuthorsText(listing.Authored.Authors), IsOwn: false))
            .OrderBy(listing => listing.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        MainViewModel.Arrange(MemberMatches, matches);
        SelectedMemberMatch = matches.FirstOrDefault(listing => listing.Id == SelectedMemberMatch?.Id) ?? (query.Length > 0 ? matches.FirstOrDefault() : null);
        OnPropertyChanged(nameof(HasNoMemberMatch));
    }

    partial void OnMemberQueryChanged(string value) => FindMembers();

    partial void OnPackVersionChanged(string value) => Refresh();

    partial void OnReleasedAtChanged(string value) => Refresh();

    partial void OnChangelogChanged(string value) => Refresh();
}
