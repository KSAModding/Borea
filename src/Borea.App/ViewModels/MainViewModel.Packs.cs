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
using Borea.Core.Licenses;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Preferences;
using Borea.Core.Stewardship;
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
    [NotifyPropertyChangedFor(nameof(CanReportPack))]
    private PackItem? _selectedPack;

    public ObservableCollection<ContentLink> PackLinks { get; } = [];

    public ObservableCollection<PackMemberItem> PackMembers { get; } = [];

    public ObservableCollection<PackVersionItem> PackVersions { get; } = [];

    public bool HasPackLinks => PackLinks.Count > 0;

    public bool HasPackTags => SelectedPack is { AllTags.Count: > 0 };

    /// <summary>The share page of the pack on the landing site, or null when it has none.</summary>
    public string? PackShareUrl => SelectedPack is { } pack ? ShareLinks.For(pack.Metadata) : null;

    public bool CanReportPack => SelectedPack is { } pack && ShareLinks.IsFromIndex(pack.Metadata.Source, pack.PackId);

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
        if (FavoritesOnly)
            filtered = filtered.Where(pack => pack.IsFavorite);
        if (InstalledInOtherInstances)
            filtered = filtered.Where(pack => pack.IsInOtherInstance);
        if (SelectedOs is not null)
            filtered = filtered.Where(pack => pack.SupportsOs(SelectedOs));
        if (SelectedLicenses.Count > 0)
            filtered = filtered.Where(pack => LicenseFilter.Matches(pack.License, SelectedLicenses));
        if (HasGameVersionRange)
            filtered = filtered.Where(pack => Borea.Core.Game.Compatibility.SupportsAnyBuild(pack.Metadata, pack.Pinned, DiscoverGameMin?.Revision, DiscoverGameMax?.Revision, _gameReleases));
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
    /// Returns whether a pack moved into or out of the other instances.
    /// </summary>
    private bool RefreshPackInstalledFlags()
    {
        var installed = ActiveInstance?.Mods ?? [];
        bool Holds(ModPackEntry pin) => HoldsPin(installed, pin);

        var elsewhereChanged = false;
        foreach (var pack in _packs)
        {
            pack.IsInstalled = pack.Metadata.Mods.All(Holds);
            var inOtherInstance = OtherInstances.Any(instance => pack.Metadata.Mods.All(pin => HoldsPin(instance.Mods, pin)));
            elsewhereChanged |= pack.IsInOtherInstance != inOtherInstance;
            pack.IsInOtherInstance = inOtherInstance;
        }
        foreach (var member in PackMembers)
            member.IsInstalled = Holds(member.Pin);
        ShowNewerMemberUse();
        foreach (var version in PackVersions)
            version.IsInstalled = version.Metadata.Mods.All(Holds);
        return elsewhereChanged;
    }

    private static bool HoldsPin(IReadOnlyList<InstalledMod> mods, ModPackEntry pin)
        => mods.Any(mod => ModIds.Equals(mod.ModId, pin.ContentId) && mod.Version == pin.Version);

    private void RefreshPackText()
    {
        foreach (var pack in _packs)
            pack.RefreshText();
        foreach (var version in PackVersions)
            version.RefreshText();
        foreach (var member in PackMembers)
            member.RefreshText();
        PackUpdate?.RefreshText();
    }

    /// <summary>Names the newer release of each member row of the open pack.</summary>
    private void ShowNewerMembers()
    {
        foreach (var member in PackMembers)
            member.NewerRelease = SelectedPack?.NewerMembers.FirstOrDefault(newer => ModIds.Equals(newer.ModId, member.ModId))?.Newer;
        ShowNewerMemberUse();
    }

    /// <summary>The header of the column that shows the state of each member in the active instance, or null without one.</summary>
    public string? PackInstanceHeaderText => ActiveInstance is { } instance ? Localization.FormatPackMemberInstanceHeader(instance.Name) : null;

    /// <summary>
    /// Shows what the active instance holds of each member, and which change the row offers. A row offers the newer
    /// release while the instance was made from the open pack and holds the member at the pinned version in files
    /// that Borea owns, and the release fits the installed game. A row offers the pinned version again while that
    /// instance holds the member detached, or as a pack member, at another version and follows the pack version that
    /// the page shows. A mod installed for another reason that the pack did not detach gets no such offer, because
    /// the next pack update changes it back.
    /// </summary>
    private void ShowNewerMemberUse()
    {
        var instance = _activeInstanceEntity;
        var pack = SelectedPack;
        var source = instance?.Source is InstanceSource.FromModPack fromPack && pack is not null && ModIds.Equals(fromPack.ModPackId, pack.PackId) ? fromPack : null;
        OnPropertyChanged(nameof(PackInstanceHeaderText));
        foreach (var member in PackMembers)
        {
            var held = instance?.Mods.FirstOrDefault(installed => ModIds.Equals(installed.ModId, member.ModId));
            var detached = held is not null && source is not null && source.Detached.Contains(held.ModId);
            var inUse = held is null || source is null ? PackMemberInUse.Plain
                : detached ? PackMemberInUse.Detached
                : held.Reason != InstallReason.ModPack ? PackMemberInUse.NextUpdate
                : PackMemberInUse.Plain;
            var owned = source is not null && held is { Ownership: ModInstallOwnership.Borea };
            var offersUse = owned && member.NewerRelease is { } newer
                && Borea.Core.Game.Compatibility.Evaluate(newer, _compatibilityGame) != GameCompatibility.Incompatible
                && held!.Version == member.Pin.Version
                && (held.Reason == InstallReason.ModPack || detached);
            // a run back that failed leaves the mod attached at the other version, so that row offers the pin again too
            var offersBack = owned && (detached || held!.Reason == InstallReason.ModPack) && member.PinRelease is not null
                && held!.Version != member.Pin.Version
                && source!.Version == pack!.Metadata.Version;
            member.ShowInstance(held is null ? null : ActiveInstance, held, inUse, offersUse, offersBack);
        }
    }

    /// <summary>
    /// Plans the newer release of a member into the instance the row names and waits for a confirmation, because the
    /// files of the pinned version go away. The plan counts the mod as unpinned and detached already, and the
    /// confirmation saves that.
    /// </summary>
    internal async Task UseNewerMemberAsync(PackMemberItem member)
    {
        if (member.IsInstalling || member.NewerRelease is not { } newer || member.UseNewerInstance is not { } target)
            return;

        var unpin = member.IsPinnedInInstance;
        member.BeginChange(PackMemberChange.UseNewer, unpin, member.Version);
        RememberRequestedVersion(member, newer.Version);
        await PlanAndExecuteAsync(
            member,
            target.InstanceId,
            instance =>
            {
                ApplyPackMemberChange(instance, member.ModId, PackMemberChange.UseNewer, unpin);
                return Task.FromResult<IReadOnlyList<RequestedMod>>([new RequestedMod(newer, InstallReason.Manual, Exact: true)]);
            },
            (_, _) => Task.FromResult(true));
    }

    /// <summary>
    /// Plans the pinned version of a member that the instance holds detached at another version, as "Attach to modpack
    /// again" on the Content tab does, and waits for a confirmation. The plan counts the mod as attached already, and
    /// the confirmation saves that.
    /// </summary>
    internal async Task BackToPackPinAsync(PackMemberItem member)
    {
        if (member.IsInstalling || member.PinRelease is not { } pinned || member.BackToPinInstance is not { } target || member.HeldVersion is not { } held)
            return;

        var unpin = member.IsPinnedInInstance;
        member.BeginChange(PackMemberChange.BackToPin, unpin, held);
        RememberRequestedVersion(member, pinned.Version);
        await PlanBackToPackPinAsync(member, target.InstanceId, pinned, instance => ApplyPackMemberChange(instance, member.ModId, PackMemberChange.BackToPin, unpin), () => { });
    }

    /// <summary>
    /// Saves the change of the pack record that the waiting plan counts on, and then runs that plan. The change is
    /// saved only while the instance still fits the plan. A run that fails or stops after it leaves the saved record,
    /// so that a Try again only has to install the release.
    /// </summary>
    internal async Task ConfirmPackMemberChangeAsync(PackMemberItem member)
    {
        if (_services is not { } services || member.IsInstalling || member.PendingChange is not { } change
            || (member.PendingPlan?.InstanceId ?? member.Choices?.InstanceId) is not { } instanceId)
            return;

        // the waiting plan stays, so the player can confirm again when the update of the instance has finished
        if (_runningUpdates.ContainsKey(instanceId))
        {
            member.InstallError = Localization.LibraryFolderInstanceBusy;
            return;
        }

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            member.InstallError = Localization.LibraryFolderBusy;
            return;
        }

        member.InstallError = null;
        var unpin = member.PendingUnpin;
        try
        {
            await services.Instances.UpdateAsync(instanceId, instance =>
            {
                var changed = ApplyPackMemberChange(instance, member.ModId, change, unpin);
                if (member.Choices is null && member.PendingPlan is { } plan && !plan.InstanceState.Matches(instance))
                    throw new InvalidOperationException(Localization.ManualInstallsInstanceChanged);
                return changed;
            });
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            CancelInstall(member);
            member.InstallError = exception.Message;
            return;
        }

        await ExecutePendingPlanAsync(member);
        await ReloadInstancesAsync();
    }

    /// <summary>Unpins the mod when the row asked for it, then detaches it from the pack or attaches it again. Returns whether the instance changed.</summary>
    private static bool ApplyPackMemberChange(Instance instance, string modId, PackMemberChange change, bool unpin)
    {
        var unpinned = unpin && instance.SetPinned(modId, pinned: false);
        var changed = change == PackMemberChange.UseNewer ? instance.DetachFromModPack(modId) : instance.AttachToModPack(modId);
        return unpinned | changed;
    }

    [RelayCommand]
    internal async Task OpenPackAsync(PackItem pack)
    {
        if (pack is null)
            return;

        SelectedPack?.ClearOutcome();
        pack.ClearOutcome();
        SelectedPack = pack;
        OpenedFrom = PageOrigin.Discover;
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
                members.Add(new PackMemberItem(this, pin, release, listing, _compatibilityGame)
                {
                    History = await _services.Mods.GetReleaseHistoryAsync(pin.ContentId),
                });
            }

            var versions = await _services.ModPacks.GetAvailableVersionsAsync(pack.PackId);
            if (!ReferenceEquals(SelectedPack, pack))
                return;

            foreach (var member in members)
                PackMembers.Add(member);
            foreach (var version in versions.Where(version => version.Metadata is not null))
                PackVersions.Add(new PackVersionItem(this, pack, version.Metadata!));
            ShowNewerMembers();
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

    /// <summary>Opens the report form of content-index for the pack. A takedown names the version the page shows, which the player can still change on GitHub.</summary>
    [RelayCommand]
    private void ReportPack(IndexReportKind kind)
    {
        if (!CanReportPack || SelectedPack is not { } pack)
            return;

        var url = IndexReport.FormUrl(kind, pack.PackId, kind == IndexReportKind.Takedown ? pack.Metadata.Version : null);
        if (TryOpenWithSystem(url.AbsoluteUri) is { } error)
            PackDetailError = error;
    }

    [RelayCommand]
    private Task CopyPackForumListAsync()
    {
        if (_services is not { } services || SelectedPack is not { } pack)
            return Task.CompletedTask;

        return CopyTextAsync(
            async () => string.Join(Environment.NewLine, await ModPackForumList.WriteAsync(pack.Metadata, services.ContentIndex)),
            () => Localization.PackForumListName,
            () => Localization.PackForumListCopied);
    }

    /// <summary>Opens the listing page with the next version of the open pack, for its author.</summary>
    [RelayCommand]
    private async Task MakeNextPackVersionAsync()
    {
        if (SelectedPack is not { } pack)
            return;

        await OpenListingAsync();
        await ListingEditor.MakeNextVersionAsync(pack.PackId);
    }

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
    {
        if ((targetInstanceId ?? ActiveInstance?.InstanceId) is { } instanceId)
            return PlanPackInstallAsync(pack, instanceId, newInstanceName: null, version, confirm);

        ShowInstanceHintToast();
        return Task.CompletedTask;
    }

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
            var pinned = await PinnedReleasesAsync(services.Mods, metadata);
            var compatibility = Borea.Core.Game.Compatibility.Evaluate(metadata, installed, _gameReleases);
            ThrowIfIncompatible(metadata, compatibility, pinned, installed);

            var instance = instanceId is { } existing
                ? await services.Instances.GetByIdAsync(existing) ?? throw new InvalidOperationException(Localization.InstallInstanceMissing)
                : NewPackInstance(newInstanceName!, metadata);
            var reasons = PackWarnings(selected, metadata, compatibility, pinned, installed);
            var yanked = new HashSet<string>(ModIds.Comparer);
            var requested = new List<RequestedMod>();
            foreach (var release in pinned)
            {
                requested.Add(new RequestedMod(release, InstallReason.ModPack, Exact: true));
                if (!release.Yanked)
                    continue;

                yanked.Add(release.ModId);
                reasons.Add(Localization.FormatPackMemberYanked(release.ModId, release.Version.ToString(), release.YankedReason));
            }

            InstallPlan? plan = null;
            InstallChoices? choices = null;
            if (requested.Count > 0)
            {
                plan = await PlanWithChoicesAsync(services, new InstallPlanningRequest(instance, requested, services.Mods, installed, services.GamePlatform.Current), null);
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
                services.GamePlatform.Current,
                ProceedWithYankedMembers: yanked.Count == 0 ? null : yanked);

            if (run.InstallStop.IsRequested)
            {
                stopped = StoppedText(pack);
            }
            else if (confirm || reasons.Count > 0 || choices is not null || (plan is { IsReady: true } && (PackPlanWarnings(plan, metadata).Count > 0 || InstallStepsOf(plan).Count > 0)))
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

    /// <summary>The releases the pack pins, in pack order. A pin whose release the index does not have is left out, because it has its own warning.</summary>
    private static async Task<List<ModVersionMetadata>> PinnedReleasesAsync(IModRepository mods, ModPackMetadata metadata)
    {
        var pinned = new List<ModVersionMetadata>();
        foreach (var pin in metadata.Mods)
        {
            if (await mods.GetReleaseAsync(pin.ContentId, pin.Version) is { } release)
                pinned.Add(release);
        }

        return pinned;
    }

    /// <summary>Blocks the install when the pack's own bounds or a pinned release do not admit the installed game, and names each of them.</summary>
    /// <param name="compatibility">The state the pack's own bounds put the installed game in.</param>
    private void ThrowIfIncompatible(ModPackMetadata metadata, GameCompatibility compatibility, IEnumerable<ModVersionMetadata> pinned, GameVersion? installed)
    {
        var reasons = PinFitTexts(pinned, installed, GameCompatibility.Incompatible);
        if (compatibility == GameCompatibility.Incompatible)
            reasons.Insert(0, Localization.FormatPackIncompatible(metadata.GameMin));
        if (reasons.Count > 0)
            throw new InvalidOperationException(string.Join(" ", reasons));
    }

    /// <summary>Names a pinned release with the bound that keeps it from fitting the installed game, or returns null when it fits.</summary>
    internal string? PinFitText(ModVersionMetadata release, GameCompatibility compatibility) => compatibility switch
    {
        GameCompatibility.Incompatible => Localization.FormatPackMemberIncompatible(release.ModId, release.Version.ToString(), release.GameMin),
        GameCompatibility.Untested when release.GameMax is { } max => Localization.FormatPackMemberUntested(release.ModId, release.Version.ToString(), max),
        _ => null,
    };

    private List<string> PinFitTexts(IEnumerable<ModVersionMetadata> pinned, GameVersion? installed, GameCompatibility compatibility)
        => pinned.Select(release => Borea.Core.Game.Compatibility.Evaluate(release, installed) == compatibility ? PinFitText(release, compatibility) : null).OfType<string>().ToList();

    /// <param name="compatibility">The state the pack's own bounds put the installed game in. The untested pins are named one by one.</param>
    private List<string> PackWarnings(ModPackResult selected, ModPackMetadata metadata, GameCompatibility compatibility, IEnumerable<ModVersionMetadata> pinned, GameVersion? installed)
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

        warnings.AddRange(PinFitTexts(pinned, installed, GameCompatibility.Untested));
        return warnings;
    }

    /// <summary>Shows the pack's own warnings, the warnings of a plan that can run or asks for choices, and what blocks that plan.</summary>
    private void HoldPack(PackItem pack, InstallPlan? plan)
    {
        var reasons = pack.PendingReasons.ToList();
        if (plan is not null && (plan.IsReady || pack.Choices is not null) && PackPlanWarnings(plan, pack.PendingInstall?.Pack.Metadata ?? pack.Metadata) is { Count: > 0 } warnings)
            reasons.Add(Describe(warnings));
        if (reasons.Count > 0 && pack.PendingInstanceName is { } name)
            reasons.Insert(0, Localization.FormatPackCreatesInstance(name));

        pack.PendingPlan = plan;
        pack.InstallWarning = reasons.Count > 0 ? string.Join(" ", reasons.Distinct()) : null;
        if (pack.Choices is { } choices && plan is not null)
            choices.BlockedText = BlockedText(plan);
    }

    /// <summary>The pack names its yanked members and its untested pins itself.</summary>
    private static List<PlanningMessage> PackPlanWarnings(InstallPlan plan, ModPackMetadata metadata)
        => plan.Warnings.Where(warning => warning.Kind != PlanningMessageKind.Yanked && !IsUntestedPin(warning, metadata)).ToList();

    private static bool IsUntestedPin(PlanningMessage warning, ModPackMetadata metadata)
        => warning is { Kind: PlanningMessageKind.Compatibility, Compatibility: GameCompatibility.Untested }
            && metadata.Mods.Any(pin => ModIds.Equals(pin.ContentId, warning.ModId));

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
        LeavePackInstallStepsNotice(pack, result);
        if (result.IsStopped)
        {
            var installed = result.Members.Count(member => member.Status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced);
            var total = result.Plan?.Operations.Count ?? 0;
            run.TaskItem.StoppedAfter = (installed, total);
            return StoppedText(pack, installed, total);
        }

        if (result.IsComplete)
            return null;

        pack.InstallError = IncompleteText(result);
        return null;
    }

    /// <summary>Names each member that kept the pack from installing and why.</summary>
    internal string IncompleteText(ModPackInstallResult result)
    {
        var reasons = result.Blockers.Select(BlockerText).Append(Describe(result.PackReasons)).Where(reason => reason.Length > 0).Distinct().ToList();
        if (reasons.Count == 0)
            reasons.Add(Localization.PackInstanceChanged);

        var failed = result.Blockers.Count > 0 ? result.Blockers.Count : result.Members.Count(member => !PackResultItem.IsDone(member.Status));
        reasons.Add(result.Members.Any(member => member.Status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced)
            ? Localization.FormatPackIncomplete(failed, result.Members.Count)
            : Localization.PackNothingInstalled);
        return string.Join(" ", reasons);
    }

    private string BlockerText(ModPackBlocker blocker)
    {
        var (modId, version) = (blocker.Member.ModId, blocker.Member.Version.ToString());
        return blocker.Member.Status == ModPackMemberStatus.Failed
            ? Localization.FormatPackMemberFailed(modId, version, blocker.Member.Message)
            : blocker.Warning switch
            {
                { Kind: PlanningMessageKind.UnlistedPin } => Localization.FormatPackMemberUnlisted(modId, version),
                { Kind: PlanningMessageKind.YankedPin } => Localization.FormatPackMemberNotConfirmed(modId, version),
                _ => Localization.FormatPackMemberUnresolved(modId, version),
            };
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

    public IReadOnlyList<string> AllTags { get; private set; }

    public int ModCount => Metadata.Mods.Count;

    public ListingImage? Icon { get; }

    public string? IconAttribution => Icon?.Attribution;

    public string? IconSource => Icon?.Source;

    internal ContentImages? Images { get; }

    public string ModCountText => _owner.Localization.FormatPackModCount(ModCount);

    /// <summary>The members that have a newer release than this version pins, in pack order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewerReleases))]
    [NotifyPropertyChangedFor(nameof(NewerReleasesText))]
    private IReadOnlyList<NewerMemberRelease> _newerMembers = [];

    public bool HasNewerReleases => NewerMembers.Count > 0;

    public string? NewerReleasesText => HasNewerReleases ? _owner.Localization.FormatPackNewerReleases(NewerMembers.Count, ModCount) : null;

    public string GameVersionText => GameVersion(Metadata);

    /// <summary>How long ago this pack version came out.</summary>
    public string ReleasedText => _owner.ShortAgeText(Metadata.ReleasedAt);

    public string ReleasedDateText => _owner.Localization.FormatPackReleasedOn(MainViewModel.DateText(Metadata.ReleasedAt));

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

    /// <summary>The releases this version pins that the index has, which decide the compatibility together with the pack's own bounds.</summary>
    internal IReadOnlyList<ModVersionMetadata> Pinned { get; private set; } = [];

    private Borea.Core.Game.GameVersion? _game;

    /// <summary>Each pinned release that does not fit the installed game, with its bound, in pack order.</summary>
    public IReadOnlyList<string> UnfitPinTexts => Pinned
        .Select(release => _owner.PinFitText(release, Borea.Core.Game.Compatibility.Evaluate(release, _game)))
        .OfType<string>()
        .ToList();

    /// <summary>False when most rows of the Modpacks tab share this state, so the row leaves the chip out.</summary>
    [ObservableProperty]
    private bool _showsCompatibility = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    /// <summary>True when an instance other than the active one holds every mod the pack pins.</summary>
    internal bool IsInOtherInstance { get; set; }

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

    private bool _isOpening;

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

    /// <summary>A plan whose members state install steps waits too, so that its confirmation can show them.</summary>
    public bool IsConfirmingInstall => InstallWarning is not null || Choices is not null || _linkRequest is not null || PlanSteps.Count > 0;

    public string ConfirmInstallText => _owner.ConfirmInstallText(InstallWarning, PendingPlan);

    /// <summary>The install steps of every listing the waiting plan installs, which the confirmation shows.</summary>
    public IReadOnlyList<StepList> PlanSteps => _owner.InstallStepsOf(PendingPlan);

    /// <summary>The install steps a finished install left on the row, until the player dismisses them.</summary>
    public IReadOnlyList<StepList> InstallStepsNotice => _owner.PackInstallStepsNotice(PackId);

    public bool HasInstallStepsNotice => InstallStepsNotice.Count > 0;

    private Func<string>? _linkRequest;

    /// <summary>What a borea:// link asked for while its confirmation waits, naming the instance. Null otherwise.</summary>
    public string? LinkRequestText => _linkRequest?.Invoke();

    internal ModPackInstallRequest? PendingInstall { get; set; }

    /// <summary>The exact version of the running install, which its task records, so the task name and a Try again keep it. Null installs the newest version.</summary>
    internal ModVersion? RequestedVersion { get; set; }

    /// <summary>The name of the instance that <see cref="PendingInstall"/> creates, or null when it installs into an existing one.</summary>
    internal string? PendingInstanceName { get; set; }

    /// <summary>The warnings about the pack itself, without those of <see cref="PendingPlan"/>.</summary>
    internal IReadOnlyList<string> PendingReasons { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    [NotifyPropertyChangedFor(nameof(PlanSteps))]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    private InstallPlan? _pendingPlan;

    public ObservableCollection<PackResultItem> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    public bool CanInstall => !IsInstalled && !IsInstalling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteText))]
    private bool _isFavorite;

    /// <summary>What the favorite toggle does, as its label and tooltip.</summary>
    public string FavoriteText => _owner.FavoriteText(IsFavorite);

    /// <param name="indexEntry">The snapshot entry of the pack, for its dates and the images of this version. Null when the snapshot has none.</param>
    public PackItem(MainViewModel owner, ModPackMetadata metadata, ContentIndexPack? indexEntry = null)
    {
        _owner = owner;
        Metadata = metadata;
        Images = MainViewModel.ImagesOf(indexEntry, metadata);
        Icon = owner.IconFor(Images?.Icon);
        AllTags = DiscoverItem.DisplayTags(owner, ContentType.ModPack, metadata.Tags);
        PublishedAt = indexEntry?.PublishedAt;
        _isFavorite = owner.IsFavoritePack(metadata.ModPackId);
    }

    internal void ShowCompatibility(IReadOnlyList<ModVersionMetadata> pinned, Borea.Core.Game.GameVersion? installed, GameReleaseList releases)
    {
        Pinned = pinned;
        _game = installed;
        Compatibility = Borea.Core.Game.Compatibility.Evaluate(Metadata, pinned, installed, releases);
        OnPropertyChanged(nameof(UnfitPinTexts));
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
        AllTags = DiscoverItem.DisplayTags(_owner, ContentType.ModPack, Metadata.Tags);
        OnPropertyChanged(nameof(AllTags));
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(CompatibilityText));
        OnPropertyChanged(nameof(UnfitPinTexts));
        OnPropertyChanged(nameof(ModCountText));
        OnPropertyChanged(nameof(NewerReleasesText));
        OnPropertyChanged(nameof(ConfirmInstallText));
        OnPropertyChanged(nameof(PlanSteps));
        OnPropertyChanged(nameof(InstallStepsNotice));
        OnPropertyChanged(nameof(ReleasedText));
        OnPropertyChanged(nameof(ReleasedDateText));
        OnPropertyChanged(nameof(PublishedText));
        OnPropertyChanged(nameof(PublishedDateText));
        OnPropertyChanged(nameof(LinkRequestText));
        OnPropertyChanged(nameof(FavoriteText));
        foreach (var result in Results)
            result.RefreshText();
    }

    internal void ShowLinkRequest(Func<string>? request)
    {
        _linkRequest = request;
        OnPropertyChanged(nameof(LinkRequestText));
        OnPropertyChanged(nameof(IsConfirmingInstall));
    }

    internal void RefreshInstallStepsNotice()
    {
        OnPropertyChanged(nameof(InstallStepsNotice));
        OnPropertyChanged(nameof(HasInstallStepsNotice));
    }

    /// <summary>
    /// The whole row is this command, so it stays executable while the page
    /// loads, because a command that cannot execute greys out every control on
    /// the row. The flag takes over the job of dropping a second click.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenAsync()
    {
        if (_isOpening)
            return;

        _isOpening = true;
        try
        {
            await _owner.OpenPackAsync(this);
        }
        finally
        {
            _isOpening = false;
        }
    }

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallPackAsync(this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmPackInstallAsync(this);

    [RelayCommand]
    private void DismissInstallStepsNotice() => _owner.DismissPackInstallStepsNotice(PackId);

    [RelayCommand]
    private void NewInstance() => _owner.BeginPackInstance(this);

    [RelayCommand]
    private Task ToggleFavoriteAsync() => _owner.ToggleFavoriteAsync(this);

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

/// <summary>What the pack does with a member that an instance holds at a version other than the pin.</summary>
internal enum PackMemberInUse
{
    /// <summary>The instance was not made from the pack, or the mod still follows the pack.</summary>
    Plain,

    /// <summary>The mod is in the detached set, so pack updates leave it alone.</summary>
    Detached,

    /// <summary>The mod has another reason than the pack, but is not detached, so the next pack update changes it back.</summary>
    NextUpdate,
}

/// <summary>The change of a pack member row that waits for a confirmation.</summary>
internal enum PackMemberChange
{
    UseNewer,
    BackToPin,
}

/// <summary>
/// One mod a pack version pins, with what the index says about that exact release.
/// </summary>
public sealed partial class PackMemberItem : ObservableObject, IInstallRow
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

    private readonly DateTimeOffset? _unavailableSince;

    /// <summary>Since when the download of the pinned release is gone from its host, or null while it downloads.</summary>
    public string? GoneText => _unavailableSince is { } since ? _owner.Localization.FormatContentVersionGone(MainViewModel.DateText(since)) : null;

    public bool CanOpen => _listing is not null;

    [ObservableProperty]
    private bool _isInstalled;

    /// <summary>The newer release of this mod, or null when the pin is the newest one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewerVersion))]
    [NotifyPropertyChangedFor(nameof(NewerText))]
    private ModVersionMetadata? _newerRelease;

    public string? NewerVersion => NewerRelease?.Version.ToString();

    public string? NewerText => NewerVersion is null ? null : _owner.Localization.FormatPackMemberNewer(NewerVersion);

    /// <summary>
    /// True while the row shows the newer release as a plain chip: it does not offer it, and the active instance
    /// does not use it already, because "1.1.10 in use" says that.
    /// </summary>
    public bool ShowsNewerAvailable => NewerRelease is { } newer && !OffersUseNewer && _held?.Version != newer.Version;

    /// <summary>The pinned release, or null when the index does not have it.</summary>
    internal ModVersionMetadata? PinRelease => _release;

    /// <summary>The active instance while it holds this mod, or null.</summary>
    private InstanceItem? _instance;

    /// <summary>The copy of this mod in <see cref="_instance"/>, or null.</summary>
    private InstalledMod? _held;

    private PackMemberInUse _inUse;

    /// <summary>The version of this mod in the active instance, or null when it does not hold the mod.</summary>
    public string? HeldVersion => _held?.Version.ToString();

    /// <summary>The active instance, while the row offers the newer release there. Null otherwise.</summary>
    internal InstanceItem? UseNewerInstance { get; private set; }

    /// <summary>The active instance, while the row offers the pinned version there again. Null otherwise.</summary>
    internal InstanceItem? BackToPinInstance { get; private set; }

    /// <summary>True while the active instance pins this mod, which keeps it at its version until a change unpins it.</summary>
    internal bool IsPinnedInInstance => _held?.IsPinned == true;

    /// <summary>True while the active instance holds this mod detached from the pack already.</summary>
    internal bool IsDetachedInInstance { get; private set; }

    /// <summary>True while the row offers to change the mod in the active instance to the newer release, which its newer chip then does.</summary>
    public bool OffersUseNewer => NewerVersion is not null && UseNewerInstance is not null;

    /// <summary>"Use 1.1.10 in Main", the tooltip and name of the chip that offers the newer release, or null.</summary>
    public string? UseNewerText => OffersUseNewer ? _owner.Localization.FormatPackMemberUseNewer(NewerVersion!, UseNewerInstance!.Name) : null;

    /// <summary>"Use 1.1.10", the text of the chip that offers the newer release, or null.</summary>
    public string? UseNewerChipText => OffersUseNewer ? _owner.Localization.FormatPackMemberUseNewerChip(NewerVersion!) : null;

    /// <summary>True while the chip that offers the newer release can start the change, which is not while a change waits or runs.</summary>
    public bool CanUseNewer => OffersUseNewer && !IsInstalling && !IsConfirmingInstall;

    /// <summary>
    /// What the confirmation of <see cref="UseNewerText"/> does to the pin and the pack, or null while the row offers no
    /// change. A mod that is neither pinned nor attached only changes its version, which the confirm button says.
    /// </summary>
    public string? UseNewerConfirmText => NewerVersion is not { } newer || UseNewerInstance is not { } instance
        ? null
        : (ShowsUnpin, IsDetachedInInstance) switch
        {
            (true, false) => _owner.Localization.FormatPackMemberUseNewerUnpinConfirm(Name, instance.Name, newer),
            (true, true) => _owner.Localization.FormatPackMemberUseNewerUnpinOnlyConfirm(Name, instance.Name, newer),
            (false, false) => _owner.Localization.FormatPackMemberUseNewerConfirm(Name, instance.Name, newer),
            (false, true) => null,
        };

    /// <summary>True while the row offers to change the mod in the active instance back to the pinned version and attach it to the pack again.</summary>
    public bool OffersBackToPin => BackToPinInstance is not null;

    /// <summary>"Back to 1.1.9 in Main", the tooltip and name of the chip that offers the pinned version again, or null.</summary>
    public string? BackToPinText => BackToPinInstance is { } instance ? _owner.Localization.FormatPackMemberBackTo(Version, instance.Name) : null;

    /// <summary>"Back to 1.1.9", the text of the chip that offers the pinned version again, or null.</summary>
    public string? BackToPinChipText => OffersBackToPin ? _owner.Localization.FormatPackMemberBackToChip(Version) : null;

    public bool CanBackToPin => OffersBackToPin && !IsInstalling && !IsConfirmingInstall;

    /// <summary>
    /// What the confirmation of <see cref="BackToPinText"/> does to the pin and the pack, or null while the row offers
    /// no change. A mod that is neither pinned nor detached only changes its version, which the confirm button says.
    /// </summary>
    public string? BackToPinConfirmText => BackToPinInstance is not { } instance
        ? null
        : (ShowsUnpin, IsDetachedInInstance) switch
        {
            (true, true) => _owner.Localization.FormatPackMemberBackToUnpinConfirm(Name, instance.Name, Version),
            (false, true) => _owner.Localization.FormatPackMemberBackToConfirm(Name, instance.Name, Version),
            (true, false) => _owner.Localization.FormatPackMemberUseNewerUnpinOnlyConfirm(Name, instance.Name, Version),
            (false, false) => null,
        };

    /// <summary>
    /// True when the confirmation names the unpin. While a change waits, that is what its click captured, because
    /// the confirmation unpins only then.
    /// </summary>
    private bool ShowsUnpin => PendingChange is not null && IsConfirmingInstall ? PendingUnpin : IsPinnedInInstance;

    /// <summary>The change that waits for a confirmation or runs, or null before the first click.</summary>
    internal PackMemberChange? PendingChange { get; private set; }

    /// <summary>True when the waiting change also unpins the mod, because the instance pinned it at the click.</summary>
    internal bool PendingUnpin { get; private set; }

    private string? _pendingReplaced;

    /// <summary>The line of the confirmation that says what the waiting change does to the pin and the pack.</summary>
    public string? ChangeConfirmText => PendingChange == PackMemberChange.BackToPin ? BackToPinConfirmText : UseNewerConfirmText;

    /// <summary>The tooltip of the Pinned chip while the active instance pins this mod, or null.</summary>
    public string? PinnedInInstanceText => _instance is { } instance && IsPinnedInInstance
        ? _owner.Localization.FormatPackMemberPinnedIn(instance.Name, HeldVersion!)
        : null;

    /// <summary>"Installed 1.1.10" while the active instance holds this mod at a version other than the pin, or null.</summary>
    public string? InUseChipText => _held is { } held && held.Version != Pin.Version ? _owner.Localization.FormatPackMemberInUse(HeldVersion!) : null;

    /// <summary>
    /// The tooltip and name of the chip with <see cref="InUseChipText"/>, which names the instance and says what the
    /// pack does with the mod. A detached mod points to the chip that attaches it again while the row has that chip.
    /// A held release that does not fit the installed game adds its bound.
    /// </summary>
    public string? InUseText => InUseChipText is null || _instance is not { } instance
        ? null
        : JoinFit(_inUse switch
        {
            PackMemberInUse.Detached when OffersBackToPin => _owner.Localization.FormatPackMemberInUseDetachedBack(instance.Name, Name, HeldVersion!, Version),
            PackMemberInUse.Detached => _owner.Localization.FormatPackMemberInUseDetached(instance.Name, Name, HeldVersion!),
            PackMemberInUse.NextUpdate => _owner.Localization.FormatPackMemberInUseNextUpdate(instance.Name, Name, HeldVersion!, Version),
            _ => _owner.Localization.FormatPackMemberInUsePlain(instance.Name, Name, HeldVersion!),
        });

    private string JoinFit(string text) => HeldRelease is { } release && _owner.PinFitText(release, HeldCompatibility) is { } fit ? $"{text} {fit}" : text;

    /// <summary>The releases of this mod, newest first, which give the fit of the version that the active instance holds.</summary>
    internal IReadOnlyList<ModVersionMetadata> History { get; init; } = [];

    private GameVersion? _game;

    /// <summary>The release that the active instance holds at a version other than the pin, or null when the index does not have it.</summary>
    private ModVersionMetadata? HeldRelease => InUseChipText is null ? null : History.FirstOrDefault(release => release.Version == _held!.Version);

    /// <summary>How the version in <see cref="InUseChipText"/> fits the installed game. The pinned release has its own chip in the Version column.</summary>
    public GameCompatibility HeldCompatibility => HeldRelease is { } release ? Borea.Core.Game.Compatibility.Evaluate(release, _game) : GameCompatibility.Unknown;

    /// <summary>True while the chip with <see cref="InUseChipText"/> shows as installed and fitting, which is when the release is neither untested nor incompatible.</summary>
    public bool IsHeldFitting => InUseChipText is not null && !IsHeldUntested && !IsHeldIncompatible;

    public bool IsHeldUntested => HeldCompatibility == GameCompatibility.Untested;

    public bool IsHeldIncompatible => HeldCompatibility == GameCompatibility.Incompatible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseNewer))]
    [NotifyPropertyChangedFor(nameof(CanBackToPin))]
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

    /// <summary>The planner's warnings while <see cref="PendingPlan"/> waits for a confirmation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    private string? _installWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(CanUseNewer))]
    [NotifyPropertyChangedFor(nameof(CanBackToPin))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    [NotifyPropertyChangedFor(nameof(AddedModsText))]
    [NotifyPropertyChangedFor(nameof(AddedModsToolTip))]
    [NotifyPropertyChangedFor(nameof(PlanSteps))]
    [NotifyPropertyChangedFor(nameof(ChangeConfirmText))]
    private InstallPlan? _pendingPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(CanUseNewer))]
    [NotifyPropertyChangedFor(nameof(CanBackToPin))]
    [NotifyPropertyChangedFor(nameof(AddedModsText))]
    [NotifyPropertyChangedFor(nameof(AddedModsToolTip))]
    [NotifyPropertyChangedFor(nameof(ChangeConfirmText))]
    private InstallChoices? _choices;

    public bool IsConfirmingInstall => PendingPlan is not null || Choices is not null;

    /// <summary>"Replace 1.1.9", because the files of the version that the instance holds go away.</summary>
    public string ConfirmInstallText => _owner.ConfirmInstallText(InstallWarning, PendingPlan, _pendingReplaced ?? Version);

    /// <summary>The other mods that the waiting plan adds. The pinned version that a change back plans counts as this mod, not as an added one.</summary>
    public string? AddedModsText => _owner.AddedModsText(PendingPlan, Choices, ownModId: ModId);

    public string? AddedModsToolTip => _owner.AddedModsText(PendingPlan, Choices, all: true, ownModId: ModId);

    /// <summary>The install steps of every listing the waiting plan installs, which the confirmation shows.</summary>
    public IReadOnlyList<StepList> PlanSteps => _owner.InstallStepsOf(PendingPlan);

    private readonly ModVersionMetadata? _release;

    /// <summary>How the pinned release fits the installed game. The row names it only when it does not fit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityText))]
    [NotifyPropertyChangedFor(nameof(FitText))]
    [NotifyPropertyChangedFor(nameof(IsUntested))]
    [NotifyPropertyChangedFor(nameof(IsIncompatible))]
    [NotifyPropertyChangedFor(nameof(HasFitChips))]
    private GameCompatibility _compatibility = GameCompatibility.Unknown;

    public string CompatibilityText => _owner.CompatibilityText(Compatibility);

    /// <summary>The pinned release with the bound that keeps it from fitting the installed game, or null when it fits.</summary>
    public string? FitText => _release is null ? null : _owner.PinFitText(_release, Compatibility);

    /// <summary>True while the row shows a chip about the pinned release, so that an empty chip line takes no room.</summary>
    public bool HasFitChips => FitText is not null || IsYanked || GoneText is not null || IsUnlisted;

    public bool IsUntested => Compatibility == GameCompatibility.Untested;

    public bool IsIncompatible => Compatibility == GameCompatibility.Incompatible;

    public PackMemberItem(MainViewModel owner, ModPackEntry pin, ModVersionMetadata? release, DiscoverItem? listing, GameVersion? installed)
    {
        _owner = owner;
        _listing = listing;
        _release = release;
        Pin = pin;
        Name = listing?.Name ?? release?.Listing?.Name ?? pin.ContentId;
        IsUnlisted = release is null;
        IsYanked = release?.Yanked == true;
        YankedReason = IsYanked ? release!.YankedReason : null;
        _unavailableSince = release?.Download.UnavailableSince;
        RefreshCompatibility(installed);
    }

    [RelayCommand]
    private Task OpenAsync() => _listing is null ? Task.CompletedTask : _owner.OpenContentAsync(_listing);

    [RelayCommand]
    private Task UseNewerAsync() => _owner.UseNewerMemberAsync(this);

    [RelayCommand]
    private Task BackToPinAsync() => _owner.BackToPackPinAsync(this);

    [RelayCommand]
    private Task ConfirmChangeAsync() => _owner.ConfirmPackMemberChangeAsync(this);

    [RelayCommand]
    private void CancelChange() => MainViewModel.CancelInstall(this);

    /// <param name="instance">The active instance while it holds this mod, or null.</param>
    /// <param name="held">The copy of this mod in that instance, or null.</param>
    /// <param name="inUse">What the pack does with a copy at a version other than the pin.</param>
    /// <param name="offersUse">True when the row offers the newer release in that instance.</param>
    /// <param name="offersBack">True when the row offers the pinned version in that instance again.</param>
    internal void ShowInstance(InstanceItem? instance, InstalledMod? held, PackMemberInUse inUse, bool offersUse, bool offersBack)
    {
        _instance = held is null ? null : instance;
        _held = _instance is null ? null : held;
        _inUse = inUse;
        UseNewerInstance = offersUse ? _instance : null;
        BackToPinInstance = offersBack ? _instance : null;
        IsDetachedInInstance = _held is not null && (inUse == PackMemberInUse.Detached || _held.Reason != InstallReason.ModPack);
        NotifyInstanceState();
    }

    /// <summary>Keeps which change the click started, so the confirmation saves that change.</summary>
    internal void BeginChange(PackMemberChange change, bool unpin, string replaced)
    {
        PendingChange = change;
        PendingUnpin = unpin;
        _pendingReplaced = replaced;
        OnPropertyChanged(nameof(ChangeConfirmText));
        OnPropertyChanged(nameof(ConfirmInstallText));
    }

    partial void OnNewerReleaseChanged(ModVersionMetadata? value) => NotifyInstanceState();

    private void NotifyInstanceState()
    {
        OnPropertyChanged(nameof(HeldVersion));
        OnPropertyChanged(nameof(ShowsNewerAvailable));
        OnPropertyChanged(nameof(OffersUseNewer));
        OnPropertyChanged(nameof(CanUseNewer));
        OnPropertyChanged(nameof(UseNewerText));
        OnPropertyChanged(nameof(UseNewerChipText));
        OnPropertyChanged(nameof(UseNewerConfirmText));
        OnPropertyChanged(nameof(OffersBackToPin));
        OnPropertyChanged(nameof(CanBackToPin));
        OnPropertyChanged(nameof(BackToPinText));
        OnPropertyChanged(nameof(BackToPinChipText));
        OnPropertyChanged(nameof(BackToPinConfirmText));
        OnPropertyChanged(nameof(ChangeConfirmText));
        OnPropertyChanged(nameof(PinnedInInstanceText));
        OnPropertyChanged(nameof(InUseChipText));
        OnPropertyChanged(nameof(InUseText));
        OnPropertyChanged(nameof(HeldCompatibility));
        OnPropertyChanged(nameof(IsHeldFitting));
        OnPropertyChanged(nameof(IsHeldUntested));
        OnPropertyChanged(nameof(IsHeldIncompatible));
    }

    internal void RefreshCompatibility(GameVersion? installed)
    {
        _game = installed;
        Compatibility = _release is null ? GameCompatibility.Unknown : Borea.Core.Game.Compatibility.Evaluate(_release, installed);
        NotifyInstanceState();
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(NewerText));
        NotifyInstanceState();
        OnPropertyChanged(nameof(ConfirmInstallText));
        OnPropertyChanged(nameof(AddedModsText));
        OnPropertyChanged(nameof(AddedModsToolTip));
        OnPropertyChanged(nameof(PlanSteps));
        OnPropertyChanged(nameof(GoneText));
        OnPropertyChanged(nameof(CompatibilityText));
        OnPropertyChanged(nameof(FitText));
    }
}

/// <summary>
/// One usable version of a pack on the Versions tab of the pack page.
/// </summary>
public sealed partial class PackVersionItem : ObservableObject
{
    private readonly MainViewModel _owner;

    internal ModPackMetadata Metadata { get; }

    /// <summary>The pack this version belongs to, which carries the state of a running install.</summary>
    public PackItem Pack { get; }

    public string Version => Metadata.Version.ToString();

    public string GameVersionText => PackItem.GameVersion(Metadata);

    /// <summary>How long ago this version came out.</summary>
    public string PublishedText => _owner.ShortAgeText(Metadata.ReleasedAt);

    public string PublishedDateText => MainViewModel.DateText(Metadata.ReleasedAt);

    public int ModCount => Metadata.Mods.Count;

    /// <summary>True when the active instance holds every mod this version pins, in the pinned version.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    public bool CanInstall => !IsInstalled;

    public PackVersionItem(MainViewModel owner, PackItem pack, ModPackMetadata metadata)
    {
        _owner = owner;
        Pack = pack;
        Metadata = metadata;
    }

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallPackAsync(Pack, version: Metadata.Version);

    [RelayCommand]
    private void NewInstance() => _owner.BeginPackInstance(Pack, Metadata.Version);

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
