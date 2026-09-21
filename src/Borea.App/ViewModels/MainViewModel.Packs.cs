using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Preferences;
using Borea.Core.Tags;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Modpacks tab of Discover and the pack page. Add installs the newest
/// usable pack version into the active instance through the pack installer
/// the CLI uses, so every member gets the mod pack install reason.
/// </summary>
public partial class MainViewModel
{
    private IReadOnlyList<PackItem> _packs = [];

    private GameReleaseList _gameReleases = GameReleaseList.Empty;

    private PackItem? _newInstancePack;

    /// <summary>The pack version the name modal creates the instance from, or null for the newest one.</summary>
    private ModVersion? _newInstancePackVersion;

    public ObservableCollection<PackItem> DiscoverPacks { get; } = [];

    public bool IsModpacksTab => DiscoverType == ContentType.ModPack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoverSection))]
    private bool _currentWindowPack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPackTags))]
    [NotifyPropertyChangedFor(nameof(PackShareUrl))]
    private PackItem? _selectedPack;

    public ObservableCollection<ContentLink> PackLinks { get; } = [];

    public ObservableCollection<PackMemberItem> PackMembers { get; } = [];

    public ObservableCollection<PackVersionItem> PackVersions { get; } = [];

    public bool HasPackLinks => PackLinks.Count > 0;

    public bool HasPackTags => SelectedPack is { Tags.Count: > 0 };

    /// <summary>The share page of the pack on the landing site, or null when it has none.</summary>
    public string? PackShareUrl => SelectedPack is { } pack ? ShareLinks.For(pack.Metadata) : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPackDescriptionTab))]
    [NotifyPropertyChangedFor(nameof(IsPackModsTab))]
    [NotifyPropertyChangedFor(nameof(IsPackVersionsTab))]
    private PackPageTab _packTab;

    public bool IsPackDescriptionTab => PackTab == PackPageTab.Description;

    public bool IsPackModsTab => PackTab == PackPageTab.Mods;

    public bool IsPackVersionsTab => PackTab == PackPageTab.Versions;

    [ObservableProperty]
    private string? _packDetailError;

    private void ApplyPackFilters(string query)
    {
        IEnumerable<PackItem> filtered = DiscoverType == ContentType.ModPack ? _packs : [];

        if (query.Length > 0)
            filtered = filtered.Where(pack => pack.Matches(query));
        if (HideInstalled)
            filtered = filtered.Where(pack => !pack.IsInstalled);
        if (HideIncompatible)
            filtered = filtered.Where(pack => pack.Compatibility != GameCompatibility.Incompatible);
        if (SelectedOs is not null)
            filtered = filtered.Where(pack => pack.SupportsOs(SelectedOs));
        if (SelectedLicense is not null)
            filtered = filtered.Where(pack => string.Equals(pack.License, SelectedLicense, StringComparison.OrdinalIgnoreCase));
        if (HasGameVersionRange)
            filtered = filtered.Where(pack => Borea.Core.Game.Compatibility.SupportsAnyBuild(pack.Metadata, DiscoverGameMin?.Revision, DiscoverGameMax?.Revision, _gameReleases));
        if (SelectedCategories.Count > 0)
        {
            var matching = ContentTagFilter.Filter(
                filtered.Select(pack => pack.Metadata),
                _categoryVocabulary,
                SelectedCategories.Where(category => !category.IsOther).Select(category => category.Tag!),
                includeOther: SelectedCategories.Any(category => category.IsOther));
            var matchingSet = new HashSet<ModPackMetadata>(matching, ReferenceEqualityComparer.Instance);
            filtered = filtered.Where(pack => matchingSet.Contains(pack.Metadata));
        }

        var rows = SortPacks(filtered).ToList();
        var common = CommonCompatibility(rows.Select(pack => pack.Compatibility).ToList());
        foreach (var pack in rows)
            pack.ShowsCompatibility = pack.Compatibility != common;
        Arrange(DiscoverPacks, rows);
    }

    /// <summary>A pack carries no download counts, so Popularity keeps the name order.</summary>
    private IEnumerable<PackItem> SortPacks(IEnumerable<PackItem> packs) => DiscoverSort == DiscoverSortOrder.RecentlyUpdated
        ? packs.OrderByDescending(pack => pack.Metadata.ReleasedAt).ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase)
        : packs.OrderBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>
    /// A pack counts as installed when the active instance holds every mod it pins, in the pinned version.
    /// </summary>
    private void RefreshPackInstalledFlags()
    {
        var installed = ActiveInstance?.Mods ?? [];
        bool Holds(ModPackEntry pin) => installed.Any(mod => ModIds.Equals(mod.ModId, pin.ContentId) && mod.Version == pin.Version);

        foreach (var pack in _packs)
            pack.IsInstalled = pack.Metadata.Mods.All(Holds);
        foreach (var member in PackMembers)
            member.IsInstalled = Holds(member.Pin);
    }

    private void RefreshPackText()
    {
        foreach (var pack in _packs)
            pack.RefreshText();
        foreach (var version in PackVersions)
            version.RefreshText();
        PackUpdate?.RefreshText();
    }

    [RelayCommand]
    internal async Task OpenPackAsync(PackItem pack)
    {
        if (pack is null)
            return;

        SelectedPack?.ClearOutcome();
        pack.ClearOutcome();
        SelectedPack = pack;
        PackDescriptionImages = new DescriptionImages(this, pack.Images);
        PackTab = PackPageTab.Description;
        PackDetailError = null;
        PackMembers.Clear();
        PackVersions.Clear();

        FillLinks(PackLinks, pack.Links);
        OnPropertyChanged(nameof(HasPackLinks));

        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        IsTasksOpen = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = true;

        await LoadPackDetailsAsync(pack);
    }

    private async Task LoadPackDetailsAsync(PackItem pack)
    {
        if (_services is null)
            return;

        try
        {
            var members = new List<PackMemberItem>();
            foreach (var pin in pack.Metadata.Mods)
            {
                var release = await _services.Mods.GetReleaseAsync(pin.ContentId, pin.Version);
                var listing = _listings.FirstOrDefault(item => ModIds.Equals(item.ModId, pin.ContentId) && item.Source == "index");
                members.Add(new PackMemberItem(this, pin, release, listing));
            }

            var versions = await _services.ModPacks.GetAvailableVersionsAsync(pack.PackId);
            if (!ReferenceEquals(SelectedPack, pack))
                return;

            foreach (var member in members)
                PackMembers.Add(member);
            foreach (var version in versions.Where(version => version.Metadata is not null))
                PackVersions.Add(new PackVersionItem(this, version.Metadata!));
            RefreshInstalledFlags();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            if (ReferenceEquals(SelectedPack, pack))
                PackDetailError = exception.Message;
        }
    }

    private void LeavePackPage()
    {
        if (!CurrentWindowPack)
            return;

        SelectedPack?.ClearOutcome();
        PackDescriptionImages = DescriptionImages.None;
    }

    [RelayCommand]
    private void ShowPackDescription() => PackTab = PackPageTab.Description;

    [RelayCommand]
    private void ShowPackMods() => PackTab = PackPageTab.Mods;

    [RelayCommand]
    private void ShowPackVersions() => PackTab = PackPageTab.Versions;

    [RelayCommand]
    private Task CopyPackShareLinkAsync() => CopyShareLinkAsync(PackShareUrl);

    [RelayCommand]
    private void OpenPackLink(ContentLink link)
    {
        if (link is not null && TryOpenUrl(link.Url) is { } error)
            PackDetailError = error;
    }

    /// <summary>
    /// Installs the newest usable version of the pack into the active instance.
    /// Any warning about the pack or its pinned releases waits on the row until the user confirms.
    /// </summary>
    /// <param name="targetInstanceId">The instance a Try again of the Tasks page installs into. Null installs into the active instance.</param>
    /// <param name="version">A usable version to install instead of the newest one.</param>
    /// <param name="confirm">Waits for the confirmation even without a warning, for an install that a borea:// link asked for.</param>
    internal Task InstallPackAsync(PackItem pack, Guid? targetInstanceId = null, ModVersion? version = null, bool confirm = false)
        => (targetInstanceId ?? ActiveInstance?.InstanceId) is { } instanceId ? PlanPackInstallAsync(pack, instanceId, newInstanceName: null, version, confirm) : Task.CompletedTask;

    /// <summary>Opens the name modal of a new instance with the name of the pack.</summary>
    /// <param name="version">The version the instance gets, or null for the newest one. An older version goes into the suggested name, so two instances of the same pack do not collide.</param>
    internal void BeginPackInstance(PackItem pack, ModVersion? version = null)
    {
        InstanceError = null;
        ModalInstanceName = version is null ? pack.Name : $"{pack.Name} {version}";
        RenamingInstance = null;
        _newInstancePack = pack;
        _newInstancePackVersion = version;
        IsCreatingInstance = true;
    }

    /// <summary>Keeps the modal open with the error when the name is taken, and otherwise installs the pack into a new instance of that name.</summary>
    private async Task CreatePackInstanceAsync(PackItem pack, string name)
    {
        if (_instances is null)
            return;

        // closing the modal clears the pinned version, so the row reads it first
        var version = _newInstancePackVersion;
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            InstanceError = Localization.LibraryFolderBusy;
            return;
        }

        string? error;
        try
        {
            error = await _instances.IsNameAvailableAsync(name) ? null : Localization.ModalNameTaken;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = exception.Message;
        }

        if (!ReferenceEquals(_newInstancePack, pack))
            return;

        if (error is not null)
        {
            InstanceError = error;
            return;
        }

        IsCreatingInstance = false;
        ModalInstanceName = string.Empty;
        await PlanPackInstallAsync(pack, instanceId: null, name, version);
    }

    /// <param name="instanceId">The instance the pack installs into, or null for a new instance.</param>
    /// <param name="newInstanceName">The name of the instance the install creates, or null.</param>
    /// <param name="version">A usable version to install instead of the newest one.</param>
    /// <param name="confirm">Waits for the confirmation even without a warning.</param>
    private async Task PlanPackInstallAsync(PackItem pack, Guid? instanceId, string? newInstanceName, ModVersion? version = null, bool confirm = false)
    {
        if (_services is null || pack.IsInstalling)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            pack.InstallError = Localization.LibraryFolderBusy;
            return;
        }

        var services = _services;
        pack.ClearOutcome();
        pack.IsInstalling = true;
        pack.RequestedVersion = version;
        var run = pack.Run = StartInstallRun(StartPackInstallTask(pack, instanceId));
        var executed = false;
        var completed = false;
        string? stopped = null;
        try
        {
            var selected = version is { } exact ? await services.ModPacks.GetVersionAsync(pack.PackId, exact) : await services.ModPacks.GetLatestAsync(pack.PackId);
            if (selected?.Metadata is not { } metadata)
                throw new InvalidOperationException(Localization.DiscoverNoRelease);

            var installed = services.InstalledVersion.GetInstalledVersion()?.Version;
            var compatibility = Borea.Core.Game.Compatibility.Evaluate(metadata, installed, _gameReleases);
            if (compatibility == GameCompatibility.Incompatible)
                throw new InvalidOperationException(Localization.FormatPackIncompatible(metadata.GameMin));

            var instance = instanceId is { } existing
                ? await services.Instances.GetByIdAsync(existing) ?? throw new InvalidOperationException(Localization.InstallInstanceMissing)
                : NewPackInstance(newInstanceName!, metadata);
            var reasons = PackWarnings(selected, metadata, compatibility);
            var yanked = new HashSet<string>(ModIds.Comparer);
            var requested = new List<RequestedMod>();
            foreach (var pin in metadata.Mods)
            {
                var release = await services.Mods.GetReleaseAsync(pin.ContentId, pin.Version);
                if (release is null)
                    continue;

                requested.Add(new RequestedMod(release, InstallReason.ModPack, Exact: true));
                if (!release.Yanked)
                    continue;

                yanked.Add(pin.ContentId);
                reasons.Add(Localization.FormatPackMemberYanked(pin.ContentId, pin.Version.ToString(), release.YankedReason));
            }

            InstallPlan? plan = null;
            InstallChoices? choices = null;
            if (requested.Count > 0)
            {
                plan = await PlanWithChoicesAsync(services, new InstallPlanningRequest(instance, requested, services.Mods, installed, CurrentPlatform()), null);
                if (InstallChoices.AreNeeded(plan))
                {
                    choices = NewChoices(instance.InstanceId, requested, plan);
                    choices.NewInstance = instanceId is null ? instance : null;
                }
            }

            var request = new ModPackInstallRequest(
                instance.InstanceId,
                selected,
                services.Mods,
                installed,
                CurrentPlatform(),
                ProceedWithYankedMembers: yanked.Count == 0 ? null : yanked);

            if (run.InstallStop.IsRequested)
            {
                stopped = StoppedText(pack);
            }
            else if (confirm || reasons.Count > 0 || choices is not null || (plan is { IsReady: true } && PackPlanWarnings(plan).Count > 0))
            {
                pack.PendingInstall = request;
                pack.PendingInstanceName = newInstanceName;
                pack.PendingReasons = reasons;
                pack.Choices = choices;
                HoldPack(pack, plan);
                if (choices is not null)
                    ReplanOnChange(pack, choices, replanned => HoldPack(pack, replanned));
            }
            else
            {
                executed = true;
                stopped = await ExecutePackInstallAsync(services, pack, request, run, newInstanceName);
                completed = true;
            }
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            pack.InstallError = exception.Message;
        }
        finally
        {
            EndInstallRun(run, completed, stopped is not null, pack.InstallError);
            pack.EndInstall(stopped);
        }

        if (executed)
            await ReloadAfterPackInstallAsync(run, newInstanceName);
    }

    private static Instance NewPackInstance(string name, ModPackMetadata metadata)
        => new(name, new InstanceSource.FromModPack(metadata.ModPackId, metadata.Version));

    /// <summary>Says so when the pack created the instance that is now active.</summary>
    private async Task ReloadAfterPackInstallAsync(InstallRun run, string? newInstanceName)
    {
        await ReloadInstancesAsync();
        if (newInstanceName is not null && run.TaskItem.InstanceId is { } created && ActiveInstance?.InstanceId == created)
            ShowSuccessToast(() => Localization.FormatLibraryNowActive(newInstanceName));
    }

    private List<string> PackWarnings(ModPackResult selected, ModPackMetadata metadata, GameCompatibility compatibility)
    {
        var warnings = new List<string>();
        foreach (var status in new[] { selected.PackStatus, selected.VersionStatus })
        {
            if (status?.State == IndexStatusState.Disputed)
                warnings.Add(Localization.FormatPackDisputed(status.Reason));
            else if (status?.State == IndexStatusState.Unknown)
                warnings.Add(Localization.FormatPackIndexStatusUnknown(status.Reason));
        }

        if (metadata.Status == ModStatus.Deprecated)
            warnings.Add(metadata.SupersededBy is null ? Localization.PackDeprecated : Localization.FormatPackSuperseded(metadata.SupersededBy));
        else if (metadata.Status == ModStatus.Unknown)
            warnings.Add(Localization.PackStatusUnknown);

        if (compatibility == GameCompatibility.Untested)
            warnings.Add(Localization.FormatPackUntested(metadata.GameMax ?? metadata.GameMin));
        else if (compatibility == GameCompatibility.Unknown)
            warnings.Add(Localization.PackCompatibilityUnknown);

        return warnings;
    }

    /// <summary>Shows the pack's own warnings, the warnings of a plan that can run or asks for choices, and what blocks that plan.</summary>
    private void HoldPack(PackItem pack, InstallPlan? plan)
    {
        var reasons = pack.PendingReasons.ToList();
        if (plan is not null && (plan.IsReady || pack.Choices is not null) && PackPlanWarnings(plan) is { Count: > 0 } warnings)
            reasons.Add(Describe(warnings));
        if (reasons.Count > 0 && pack.PendingInstanceName is { } name)
            reasons.Insert(0, Localization.FormatPackCreatesInstance(name));

        pack.PendingPlan = plan;
        pack.InstallWarning = reasons.Count > 0 ? string.Join(" ", reasons.Distinct()) : null;
        if (pack.Choices is { } choices && plan is not null)
            choices.BlockedText = BlockedText(plan);
    }

    /// <summary>The pack names its yanked members itself.</summary>
    private static List<PlanningMessage> PackPlanWarnings(InstallPlan plan)
        => plan.Warnings.Where(warning => warning.Kind != PlanningMessageKind.Yanked).ToList();

    /// <summary>Installs the pack the row holds, unless the plan with its choices asks something new, cannot run, or has a new warning.</summary>
    internal async Task ConfirmPackInstallAsync(PackItem pack)
    {
        if (_services is null || pack.PendingInstall is not { } request || pack.IsInstalling)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            if (pack.Choices is { } shown)
                shown.BlockedText = Localization.LibraryFolderBusy;
            else
                pack.InstallError = Localization.LibraryFolderBusy;
            return;
        }

        var services = _services;
        var newInstanceName = pack.PendingInstanceName;
        pack.IsInstalling = true;
        var run = pack.Run = StartInstallRun(StartPackInstallTask(pack, newInstanceName is null ? request.InstanceId : null));
        var executed = false;
        var completed = false;
        string? stopped = null;
        string? error = null;
        int? revision = null;
        try
        {
            if (newInstanceName is not null && !await services.Instances.IsNameAvailableAsync(newInstanceName))
                throw new InvalidOperationException(Localization.ModalNameTaken);

            if (pack.Choices is { } choices)
            {
                await WhenPlanningEndedAsync(choices);
                revision = choices.Revision;
                var shown = (pack.PendingPlan ?? choices.ShownPlan)?.Warnings ?? [];
                var instance = newInstanceName is null
                    ? await services.Instances.GetByIdAsync(choices.InstanceId) ?? throw new InvalidOperationException(Localization.InstallInstanceMissing)
                    : choices.NewInstance ?? NewPackInstance(newInstanceName, request.Pack.Metadata!);
                var plan = await PlanWithChoicesAsync(services, PlanningRequest(services, instance, choices.Requested), choices);
                if (revision != choices.Revision)
                    return;

                if (choices.Apply(plan) || !plan.IsReady || !plan.Warnings.All(shown.Contains))
                {
                    HoldPack(pack, plan);
                    return;
                }

                request = request with { Recommended = choices.SelectedRecommendations, Alternatives = choices.SelectedAlternatives };
            }

            pack.CancelInstall();
            executed = true;
            stopped = await ExecutePackInstallAsync(services, pack, request, run, newInstanceName);
            error = pack.InstallError;
            completed = true;
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = exception.Message;
            if (pack.Choices is { } choices)
                choices.BlockedText = error;
            else
                pack.InstallError = error;
        }
        finally
        {
            EndInstallRun(run, completed, stopped is not null, error);
            pack.EndInstall(stopped);
            if (error is null)
                ReplanIfChanged(pack, revision);
        }

        if (executed)
            await ReloadAfterPackInstallAsync(run, newInstanceName);
    }

    /// <summary>Returns what the row shows after a stop, or null when nothing stopped the pack.</summary>
    private async Task<string?> ExecutePackInstallAsync(BoreaServices services, PackItem pack, ModPackInstallRequest request, InstallRun run, string? newInstanceName)
    {
        run.TaskItem.MarkRunning();
        var result = newInstanceName is null
            ? await services.ModPackInstaller.InstallAsync(request, ProgressOf(pack), run.InstallStop)
            : await services.ModPackInstaller.CreateAndInstallAsync(newInstanceName, request, ProgressOf(pack), run.InstallStop);
        if (newInstanceName is not null && result.InstanceId != Guid.Empty)
            run.TaskItem.SetInstance(result.InstanceId, newInstanceName);
        pack.ShowResults(result.Members.Select(member => new PackResultItem(this, member)));
        if (result.IsStopped)
        {
            var installed = result.Members.Count(member => member.Status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced);
            var total = result.Plan?.Operations.Count ?? 0;
            run.TaskItem.StoppedAfter = (installed, total);
            return StoppedText(pack, installed, total);
        }

        if (result.IsComplete)
            return null;

        var incomplete = result.Members.Count(member => !PackResultItem.IsDone(member.Status));
        var summary = Localization.FormatPackIncomplete(incomplete, result.Members.Count);
        var details = result.Plan is null ? string.Empty : Describe(result.Plan.Conflicts.Concat(result.Plan.UnresolvedChoices));
        pack.InstallError = details.Length == 0 ? summary : $"{summary} {details}";
        return null;
    }
}

public enum PackPageTab
{
    Description,
    Mods,
    Versions,
}

/// <summary>
/// One row of the Modpacks tab, and the pack the pack page shows.
/// </summary>
public sealed partial class PackItem : ObservableObject, IPlanRow
{
    private readonly MainViewModel _owner;

    internal ModPackMetadata Metadata { get; }

    public string PackId => Metadata.ModPackId;

    public string Name => Metadata.Name;

    public string Abstract => Metadata.Abstract;

    public string? Description => Metadata.Description;

    public string License => Metadata.License;

    public string Version => Metadata.Version.ToString();

    public IReadOnlyDictionary<string, string> Links => Metadata.Links;

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<string> AllTags { get; }

    public int ModCount => Metadata.Mods.Count;

    public ListingImage? Icon { get; }

    public string? IconAttribution => Icon?.Attribution;

    public string? IconSource => Icon?.Source;

    internal ContentImages? Images { get; }

    public string ModCountText => _owner.Localization.FormatPackModCount(ModCount);

    public string GameVersionText => GameVersion(Metadata);

    /// <summary>How long ago this pack version came out.</summary>
    public string ReleasedText => _owner.ShortAgeText(Metadata.ReleasedAt);

    public string ReleasedDateText => MainViewModel.DateText(Metadata.ReleasedAt);

    /// <summary>The date of the first pack version the index reports, or null when it reports none.</summary>
    public DateTimeOffset? PublishedAt { get; }

    public string? PublishedText => PublishedAt is { } at ? _owner.Localization.FormatContentPublished(_owner.AgeText(at)) : null;

    public string? PublishedDateText => PublishedAt is { } at ? MainViewModel.DateText(at) : null;

    public string TypeText => _owner.Localization.ContentTypeModPack;

    public string AuthorNames => string.Join(", ", Metadata.Authors);

    public string AuthorsText => _owner.Localization.FormatContentByAuthor(AuthorNames);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityText))]
    [NotifyPropertyChangedFor(nameof(IsCompatible))]
    [NotifyPropertyChangedFor(nameof(IsUntested))]
    [NotifyPropertyChangedFor(nameof(IsIncompatible))]
    private GameCompatibility _compatibility = GameCompatibility.Unknown;

    public string CompatibilityText => Compatibility switch
    {
        GameCompatibility.Compatible => _owner.Localization.CompatibilityCompatible,
        GameCompatibility.Untested => _owner.Localization.CompatibilityUntested,
        GameCompatibility.Incompatible => _owner.Localization.CompatibilityIncompatible,
        _ => _owner.Localization.CompatibilityUnknown,
    };

    public bool IsCompatible => Compatibility == GameCompatibility.Compatible;

    public bool IsUntested => Compatibility == GameCompatibility.Untested;

    public bool IsIncompatible => Compatibility == GameCompatibility.Incompatible;

    /// <summary>False when most rows of the Modpacks tab share this state, so the row leaves the chip out.</summary>
    [ObservableProperty]
    private bool _showsCompatibility = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressStatus;

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private InstallRun? _run;

    [ObservableProperty]
    private string? _installError;

    /// <summary>
    /// The warnings while <see cref="PendingInstall"/> waits for a confirmation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    private string? _installWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    private InstallChoices? _choices;

    public bool IsConfirmingInstall => InstallWarning is not null || Choices is not null || _linkRequest is not null;

    public string ConfirmInstallText => _owner.ConfirmInstallText(InstallWarning, PendingPlan);

    private Func<string>? _linkRequest;

    /// <summary>What a borea:// link asked for while its confirmation waits, naming the instance. Null otherwise.</summary>
    public string? LinkRequestText => _linkRequest?.Invoke();

    internal ModPackInstallRequest? PendingInstall { get; set; }

    /// <summary>The version a borea:// link pinned for the last install, so Try again keeps it. Null for the newest.</summary>
    internal ModVersion? RequestedVersion { get; set; }

    /// <summary>The name of the instance that <see cref="PendingInstall"/> creates, or null when it installs into an existing one.</summary>
    internal string? PendingInstanceName { get; set; }

    /// <summary>The warnings about the pack itself, without those of <see cref="PendingPlan"/>.</summary>
    internal IReadOnlyList<string> PendingReasons { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    private InstallPlan? _pendingPlan;

    public ObservableCollection<PackResultItem> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    public bool CanInstall => !IsInstalled && !IsInstalling;

    /// <param name="indexEntry">The snapshot entry of the pack, for its dates and the images of this version. Null when the snapshot has none.</param>
    public PackItem(MainViewModel owner, ModPackMetadata metadata, ContentIndexPack? indexEntry = null)
    {
        _owner = owner;
        Metadata = metadata;
        Images = MainViewModel.ImagesOf(indexEntry, metadata);
        Icon = owner.IconFor(Images?.Icon);
        AllTags = DiscoverItem.DisplayTags(owner.TagVocabulary, ContentType.ModPack, metadata.Tags);
        Tags = AllTags.Take(3).ToList();
        PublishedAt = indexEntry?.PublishedAt;
    }

    internal static string GameVersion(ModPackMetadata pack)
        => pack.GameMax is null ? $">= {pack.GameMin}" : $"{pack.GameMin} - {pack.GameMax}";

    internal bool Matches(string query)
        => Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Abstract.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Metadata.Authors.Any(author => author.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            || Metadata.Tags.Concat(AllTags).Any(tag => tag.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    internal bool SupportsOs(string os)
        => Metadata.Os is null || Metadata.Os.Contains(os, StringComparer.OrdinalIgnoreCase);

    internal void ShowResults(IEnumerable<PackResultItem> results)
    {
        Results.Clear();
        foreach (var result in results)
            Results.Add(result);
        OnPropertyChanged(nameof(HasResults));
    }

    internal void ClearOutcome()
    {
        InstallError = null;
        ProgressStatus = null;
        CancelInstall();
        ShowResults([]);
    }

    /// <param name="stopped">What the row shows after a stop, or null.</param>
    internal void EndInstall(string? stopped = null)
    {
        IsInstalling = false;
        Run = null;
        Progress = 0;
        ProgressStatus = stopped;
        ProgressDetail = null;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(CompatibilityText));
        OnPropertyChanged(nameof(ModCountText));
        OnPropertyChanged(nameof(ConfirmInstallText));
        OnPropertyChanged(nameof(ReleasedText));
        OnPropertyChanged(nameof(ReleasedDateText));
        OnPropertyChanged(nameof(PublishedText));
        OnPropertyChanged(nameof(PublishedDateText));
        OnPropertyChanged(nameof(LinkRequestText));
        foreach (var result in Results)
            result.RefreshText();
    }

    internal void ShowLinkRequest(Func<string>? request)
    {
        _linkRequest = request;
        OnPropertyChanged(nameof(LinkRequestText));
        OnPropertyChanged(nameof(IsConfirmingInstall));
    }

    [RelayCommand]
    private Task OpenAsync() => _owner.OpenPackAsync(this);

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallPackAsync(this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmPackInstallAsync(this);

    [RelayCommand]
    private void NewInstance() => _owner.BeginPackInstance(this);

    [RelayCommand]
    internal void CancelInstall()
    {
        PendingInstall = null;
        PendingInstanceName = null;
        PendingReasons = [];
        PendingPlan = null;
        InstallWarning = null;
        Choices = null;
        ShowLinkRequest(null);
    }
}

/// <summary>
/// One mod a pack version pins, with what the index says about that exact release.
/// </summary>
public sealed partial class PackMemberItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly DiscoverItem? _listing;

    internal ModPackEntry Pin { get; }

    public string ModId => Pin.ContentId;

    public string Name { get; }

    public string Version => Pin.Version.ToString();

    public bool IsUnlisted { get; }

    public bool IsYanked { get; }

    public string? YankedReason { get; }

    public bool CanOpen => _listing is not null;

    [ObservableProperty]
    private bool _isInstalled;

    public PackMemberItem(MainViewModel owner, ModPackEntry pin, ModVersionMetadata? release, DiscoverItem? listing)
    {
        _owner = owner;
        _listing = listing;
        Pin = pin;
        Name = listing?.Name ?? release?.Listing?.Name ?? pin.ContentId;
        IsUnlisted = release is null;
        IsYanked = release?.Yanked == true;
        YankedReason = IsYanked ? release!.YankedReason : null;
    }

    [RelayCommand]
    private Task OpenAsync() => _listing is null ? Task.CompletedTask : _owner.OpenContentAsync(_listing);
}

/// <summary>
/// One usable version of a pack on the Versions tab of the pack page.
/// </summary>
public sealed class PackVersionItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly ModPackMetadata _pack;

    public string Version => _pack.Version.ToString();

    public string GameVersionText => PackItem.GameVersion(_pack);

    /// <summary>How long ago this version came out.</summary>
    public string PublishedText => _owner.ShortAgeText(_pack.ReleasedAt);

    public string PublishedDateText => MainViewModel.DateText(_pack.ReleasedAt);

    public int ModCount => _pack.Mods.Count;

    public PackVersionItem(MainViewModel owner, ModPackMetadata pack)
    {
        _owner = owner;
        _pack = pack;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(PublishedText));
        OnPropertyChanged(nameof(PublishedDateText));
    }
}

/// <summary>
/// What the last pack install did with one mod.
/// </summary>
public sealed partial class PackResultItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly ModPackMemberResult _member;

    public string ModId => _member.ModId;

    public string Version => _member.Version.ToString();

    public ModPackMemberStatus Status => _member.Status;

    public string? Message => _member.Message;

    public bool IsSuccess => IsDone(Status);

    public string StatusText => Status switch
    {
        ModPackMemberStatus.Installed => _owner.Localization.PackResultInstalled,
        ModPackMemberStatus.Replaced => _owner.Localization.PackResultReplaced,
        ModPackMemberStatus.AlreadyInstalled => _owner.Localization.PackResultAlreadyInstalled,
        ModPackMemberStatus.Unresolved => _owner.Localization.PackResultUnresolved,
        ModPackMemberStatus.Failed => _owner.Localization.PackResultFailed,
        _ => _owner.Localization.PackResultNotAttempted,
    };

    public PackResultItem(MainViewModel owner, ModPackMemberResult member)
    {
        _owner = owner;
        _member = member;
    }

    internal static bool IsDone(ModPackMemberStatus status)
        => status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced or ModPackMemberStatus.AlreadyInstalled;

    internal void RefreshText() => OnPropertyChanged(nameof(StatusText));
}
