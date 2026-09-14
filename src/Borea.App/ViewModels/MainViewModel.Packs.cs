using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
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

    public ObservableCollection<PackItem> DiscoverPacks { get; } = [];

    public bool IsModpacksTab => DiscoverType == ContentType.ModPack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoverSection))]
    private bool _currentWindowPack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPackTags))]
    private PackItem? _selectedPack;

    public ObservableCollection<ContentLink> PackLinks { get; } = [];

    public ObservableCollection<PackMemberItem> PackMembers { get; } = [];

    public ObservableCollection<PackVersionItem> PackVersions { get; } = [];

    public bool HasPackLinks => PackLinks.Count > 0;

    public bool HasPackTags => SelectedPack is { Tags.Count: > 0 };

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

        DiscoverPacks.Clear();
        foreach (var pack in filtered)
            DiscoverPacks.Add(pack);
    }

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
    }

    [RelayCommand]
    internal async Task OpenPackAsync(PackItem pack)
    {
        if (pack is null)
            return;

        SelectedPack?.ClearOutcome();
        pack.ClearOutcome();
        SelectedPack = pack;
        PackTab = PackPageTab.Description;
        PackDetailError = null;
        PackMembers.Clear();
        PackVersions.Clear();

        PackLinks.Clear();
        foreach (var link in pack.Links.OrderBy(link => LinkOrder(link.Key)))
            PackLinks.Add(new ContentLink(LinkLabel(link.Key), link.Value));
        OnPropertyChanged(nameof(HasPackLinks));

        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = false;
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
                PackVersions.Add(new PackVersionItem(version.Metadata!));
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
        if (CurrentWindowPack)
            SelectedPack?.ClearOutcome();
    }

    [RelayCommand]
    private void ShowPackDescription() => PackTab = PackPageTab.Description;

    [RelayCommand]
    private void ShowPackMods() => PackTab = PackPageTab.Mods;

    [RelayCommand]
    private void ShowPackVersions() => PackTab = PackPageTab.Versions;

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
    internal async Task InstallPackAsync(PackItem pack)
    {
        if (_services is null || ActiveInstance is null || pack.IsInstalling)
            return;

        var services = _services;
        var instanceId = ActiveInstance.InstanceId;
        pack.ClearOutcome();
        pack.IsInstalling = true;
        var executed = false;
        try
        {
            var selected = await services.ModPacks.GetLatestAsync(pack.PackId);
            if (selected?.Metadata is not { } metadata)
                throw new InvalidOperationException(Localization.DiscoverNoRelease);

            var installed = services.InstalledVersion.GetInstalledVersion()?.Version;
            var compatibility = Borea.Core.Game.Compatibility.Evaluate(metadata, installed);
            if (compatibility == GameCompatibility.Incompatible)
                throw new InvalidOperationException(Localization.FormatPackIncompatible(metadata.GameMin));

            var instance = await services.Instances.GetByIdAsync(instanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
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

            if (requested.Count > 0)
            {
                var plan = await services.InstallPlanner.PlanAsync(
                    new InstallPlanningRequest(instance, requested, services.Mods, installed, CurrentPlatform()));
                var planWarnings = plan.Warnings.Where(warning => warning.Code != "yanked").ToList();
                if (plan.IsReady && planWarnings.Count > 0)
                    reasons.Add(Describe(plan, planWarnings));
            }

            var request = new ModPackInstallRequest(
                instanceId,
                selected,
                services.Mods,
                installed,
                CurrentPlatform(),
                ProceedWithYankedMembers: yanked.Count == 0 ? null : yanked);

            if (reasons.Count > 0)
            {
                pack.PendingInstall = request;
                pack.InstallWarning = string.Join(" ", reasons.Distinct());
            }
            else
            {
                executed = true;
                await ExecutePackInstallAsync(services, pack, request);
            }
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            pack.InstallError = exception.Message;
        }
        finally
        {
            pack.IsInstalling = false;
        }

        if (executed)
            await ReloadInstancesAsync();
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

    internal async Task ConfirmPackInstallAsync(PackItem pack)
    {
        if (_services is null || pack.PendingInstall is not { } request || pack.IsInstalling)
            return;

        pack.PendingInstall = null;
        pack.InstallWarning = null;
        pack.IsInstalling = true;
        try
        {
            await ExecutePackInstallAsync(_services, pack, request);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            pack.InstallError = exception.Message;
        }
        finally
        {
            pack.IsInstalling = false;
        }

        await ReloadInstancesAsync();
    }

    private async Task ExecutePackInstallAsync(BoreaServices services, PackItem pack, ModPackInstallRequest request)
    {
        var result = await services.ModPackInstaller.InstallAsync(request);
        pack.ShowResults(result.Members.Select(member => new PackResultItem(this, member)));
        if (result.IsComplete)
            return;

        var incomplete = result.Members.Count(member => !PackResultItem.IsDone(member.Status));
        var summary = Localization.FormatPackIncomplete(incomplete, result.Members.Count);
        var details = result.Plan is null ? string.Empty : Describe(result.Plan, result.Plan.Conflicts.Concat(result.Plan.UnresolvedChoices));
        pack.InstallError = details.Length == 0 ? summary : $"{summary} {details}";
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
public sealed partial class PackItem : ObservableObject
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

    public string ModCountText => _owner.Localization.FormatPackModCount(ModCount);

    public string GameVersionText => GameVersion(Metadata);

    public string PublishedText => Metadata.ReleasedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    public string TypeText => _owner.Localization.ContentTypeModPack;

    public string AuthorNames => string.Join(", ", Metadata.Authors);

    public string AuthorsText => _owner.Localization.FormatContentByAuthor(AuthorNames);

    public string SourceText => _owner.Localization.FormatContentSource(Metadata.Source == "index" ? _owner.Localization.SourceContentIndex : Metadata.Source);

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalling;

    [ObservableProperty]
    private string? _installError;

    /// <summary>
    /// The warnings while <see cref="PendingInstall"/> waits for a confirmation.
    /// </summary>
    [ObservableProperty]
    private string? _installWarning;

    internal ModPackInstallRequest? PendingInstall { get; set; }

    public ObservableCollection<PackResultItem> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    public bool CanInstall => !IsInstalled && !IsInstalling;

    public PackItem(MainViewModel owner, ModPackMetadata metadata)
    {
        _owner = owner;
        Metadata = metadata;
        AllTags = DiscoverItem.DisplayTags(owner.TagVocabulary, ContentType.ModPack, metadata.Tags);
        Tags = AllTags.Take(3).ToList();
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
        InstallWarning = null;
        PendingInstall = null;
        ShowResults([]);
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(CompatibilityText));
        OnPropertyChanged(nameof(ModCountText));
        foreach (var result in Results)
            result.RefreshText();
    }

    [RelayCommand]
    private Task OpenAsync() => _owner.OpenPackAsync(this);

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallPackAsync(this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmPackInstallAsync(this);

    [RelayCommand]
    private void CancelInstall()
    {
        PendingInstall = null;
        InstallWarning = null;
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
public sealed class PackVersionItem
{
    private readonly ModPackMetadata _pack;

    public string Version => _pack.Version.ToString();

    public string GameVersionText => PackItem.GameVersion(_pack);

    public string PublishedText => _pack.ReleasedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    public int ModCount => _pack.Mods.Count;

    public PackVersionItem(ModPackMetadata pack) => _pack = pack;
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
