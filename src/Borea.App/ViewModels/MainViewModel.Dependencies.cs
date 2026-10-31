using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Dependencies;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Dependencies tab of the content page: what the release in the header
/// needs, grouped by kind (RFC 0031), the mods that conflict with this mod, and
/// the listings that need this mod.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<DependencyGroup> ContentDependencyGroups { get; } = [];

    /// <summary>The loader the release in the header needs, or null when it needs none.</summary>
    public DependencyMember? ContentLoaderDependency { get; private set; }

    /// <summary>The index listings whose newest release in the saved channel needs this mod.</summary>
    private List<DependencyMember> _requiredBy = [];

    private bool _showsAllContentRequiredBy;

    /// <summary>The rows the Required by card shows, the listings that the active instance holds or all of them after Show all.</summary>
    public ObservableCollection<DependencyMember> ContentRequiredBy { get; } = [];

    public bool HasContentRequiredBy => _requiredBy.Count > 0;

    public bool IsContentRequiredByNoneInstalled => HasContentRequiredBy && ContentRequiredBy.Count == 0;

    public string ContentRequiredByToggleText => _showsAllContentRequiredBy
        ? Localization.ContentDependencyRequiredByShowInstalled
        : Localization.FormatContentDependencyRequiredByShowAll(_requiredBy.Count);

    public ObservableCollection<DependencyMember> ContentConflicts { get; } = [];

    public bool HasContentConflicts => ContentConflicts.Count > 0;

    public string? ContentDependenciesHeading => LatestVersion is { } release ? Localization.FormatContentDependenciesOf(release.Version) : null;

    public string? ContentDependenciesEmptyText => LatestVersion is null
        ? Localization.ContentDependenciesNoRelease
        : ContentDependencyGroups.Count == 0 ? Localization.ContentNoDependencies : null;

    /// <summary>
    /// A loader page shows the tab only when its release has dependencies or a
    /// conflict, or a listing names the loader in a required or conflict entry.
    /// The loader that a release runs on does not count.
    /// </summary>
    public bool HasContentDependenciesTab => !IsLoaderContent || ContentDependencyGroups.Count > 0 || HasContentRequiredBy || HasContentConflicts;

    /// <summary>A vehicle or save depends only on mods, so its tab says so.</summary>
    public string ContentDependenciesTabText => SelectedContent?.Type.IsVehicleOrSave() == true ? Localization.ContentTabNeedsMods : Localization.ContentTabDependencies;

    /// <summary>The oldest game build that loads the file of a vehicle or save, from the release in the header. Null for any other type.</summary>
    public string? ContentFileBuildText => SelectedContent?.Type.IsVehicleOrSave() == true && LatestVersion?.Release.GameMin is { } build ? Localization.FormatContentFileBuild(build) : null;

    [RelayCommand]
    private void ShowContentDependencies() => ContentTab = ContentPageTab.Dependencies;

    [RelayCommand]
    private void ToggleContentRequiredBy()
    {
        _showsAllContentRequiredBy = !_showsAllContentRequiredBy;
        ShowContentRequiredByRows();
    }

    // a page without a release does not change LatestVersion, so the Required by and Conflicts with cards follow the page here
    partial void OnSelectedContentChanged(DiscoverItem? value)
    {
        _showsAllContentRequiredBy = false;
        RefreshContentDependencies();
    }

    private void RefreshContentDependencies()
    {
        var release = LatestVersion?.Release;
        ContentDependencyGroups.Clear();
        if (release is not null)
        {
            foreach (var group in BuildDependencyGroups(release))
                ContentDependencyGroups.Add(group);
        }
        ContentLoaderDependency = release?.Loader is { } loader ? LoaderMember(loader) : null;
        _requiredBy = [];
        ContentConflicts.Clear();
        if (SelectedContent is { } content)
        {
            _requiredBy = RequiredByMembers(content.ModId).ToList();
            foreach (var member in BuildConflictMembers(content.ModId, release))
                ContentConflicts.Add(member);
        }
        ShowContentRequiredByRows();

        if (IsDependenciesTab && !HasContentDependenciesTab)
            ContentTab = ContentPageTab.Description;

        OnPropertyChanged(nameof(ContentLoaderDependency));
        OnPropertyChanged(nameof(ContentDependenciesHeading));
        OnPropertyChanged(nameof(ContentDependenciesEmptyText));
        OnPropertyChanged(nameof(HasContentRequiredBy));
        OnPropertyChanged(nameof(HasContentConflicts));
        OnPropertyChanged(nameof(HasContentDependenciesTab));
        OnPropertyChanged(nameof(ContentDependenciesTabText));
        OnPropertyChanged(nameof(ContentFileBuildText));
    }

    private void ShowContentRequiredByRows()
    {
        ContentRequiredBy.Clear();
        foreach (var member in _showsAllContentRequiredBy ? _requiredBy : _requiredBy.Where(dependent => dependent.IsInstalled))
            ContentRequiredBy.Add(member);

        OnPropertyChanged(nameof(IsContentRequiredByNoneInstalled));
        OnPropertyChanged(nameof(ContentRequiredByToggleText));
    }

    /// <summary>The groups in the order of #392 without Conflicts, which has its own card, each only when it has entries.</summary>
    internal IReadOnlyList<DependencyGroup> BuildDependencyGroups(ModVersionMetadata release)
    {
        var instance = ActiveInstance;
        var isItem = release.Type.IsVehicleOrSave();
        var inactive = InactiveForItem(release, instance);
        return Enum.GetValues<DependencyGroupKind>()
            .Select(kind => new DependencyGroup(
                kind,
                GroupHeading(kind),
                GroupHint(kind, isItem),
                release.Dependencies.Where(dependency => GroupOf(dependency) == kind).Select(dependency => Entry(dependency, instance, inactive)).ToList()))
            .Where(group => group.Entries.Count > 0)
            .ToList();
    }

    /// <summary>The mods that the instance holds disabled when <paramref name="release"/> is a vehicle or save, because the game loads no disabled mod and such a page says so. Null for any other type.</summary>
    private IReadOnlySet<string>? InactiveForItem(ModVersionMetadata? release, InstanceItem? instance)
        => release?.Type.IsVehicleOrSave() == true && instance is not null ? InactiveModIds(instance).ToHashSet(ModIds.Comparer) : null;

    /// <summary>A conflict has no group, because the Conflicts with card lists it.</summary>
    private static DependencyGroupKind? GroupOf(ModDependency dependency) => dependency.Kind switch
    {
        ModDependencyKind.Required => dependency.IsAnyOf ? DependencyGroupKind.AnyOf : DependencyGroupKind.Required,
        ModDependencyKind.Conflict => null,
        ModDependencyKind.Recommends => DependencyGroupKind.Recommended,
        ModDependencyKind.Suggests => DependencyGroupKind.Suggested,
        ModDependencyKind.Optional => DependencyGroupKind.Optional,
        _ => DependencyGroupKind.Other,
    };

    private string GroupHeading(DependencyGroupKind kind) => kind switch
    {
        DependencyGroupKind.Required => Localization.ContentDependencyRequired,
        DependencyGroupKind.AnyOf => Localization.ContentDependencyAnyOf,
        DependencyGroupKind.Recommended => Localization.ContentDependencyRecommended,
        DependencyGroupKind.Suggested => Localization.ContentDependencySuggested,
        DependencyGroupKind.Optional => Localization.ContentDependencyOptional,
        _ => Localization.ContentDependencyOther,
    };

    /// <param name="isItem">A vehicle or save, whose hints do not call it a mod.</param>
    private string GroupHint(DependencyGroupKind kind, bool isItem) => kind switch
    {
        DependencyGroupKind.Required => isItem ? Localization.ContentDependencyRequiredItemHint : Localization.ContentDependencyRequiredHint,
        DependencyGroupKind.AnyOf => isItem ? Localization.ContentDependencyAnyOfItemHint : Localization.ContentDependencyAnyOfHint,
        DependencyGroupKind.Recommended => isItem ? Localization.ContentDependencyRecommendedItemHint : Localization.ContentDependencyRecommendedHint,
        DependencyGroupKind.Suggested => Localization.ContentDependencySuggestedHint,
        DependencyGroupKind.Optional => isItem ? Localization.ContentDependencyOptionalItemHint : Localization.ContentDependencyOptionalHint,
        _ => Localization.ContentDependencyOtherHint,
    };

    private DependencyEntry Entry(ModDependency dependency, InstanceItem? instance, IReadOnlySet<string>? inactive) => dependency.IsAnyOf
        ? new DependencyEntry(true, GroupOf(dependency) != DependencyGroupKind.AnyOf, dependency.AnyOf.Select(member => Member(member.ModId, member.MinVersion, member.MaxVersion, instance, inactive)).ToList())
        : new DependencyEntry(false, false, [Member(dependency.ModId, dependency.MinVersion, dependency.MaxVersion, instance, inactive)]);

    private DependencyMember Member(string modId, ModVersion? min, ModVersion? max, InstanceItem? instance, IReadOnlySet<string>? inactive)
    {
        var installed = instance?.InstalledVersionOf(modId);
        return new DependencyMember(this, modId, IndexListing(modId), BoundsText(min, max), NamedStateText(modId, instance, installed, inactive), installed is not null, isConflicting: false);
    }

    private string? NamedStateText(string modId, InstanceItem? instance, ModVersion? installed, IReadOnlySet<string>? inactive)
        => installed is { } off && inactive?.Contains(modId) == true ? Localization.FormatContentDependencyDisabled(off.ToString()) : InstallStateText(instance, installed);

    private string? InstallStateText(InstanceItem? instance, ModVersion? installed) => instance is null ? null
        : installed is { } present ? Localization.FormatContentDependencyInstalled(present.ToString())
        : Localization.ContentDependencyNotInstalled;

    /// <summary>The loader is not part of an instance, so its row has no state.</summary>
    private DependencyMember LoaderMember(LoaderRequirement loader)
    {
        var listing = IndexListing(loader.LoaderId);
        return new DependencyMember(this, loader.LoaderId, listing, boundsText: null, stateText: null, isInstalled: false, isConflicting: false)
        {
            Title = Localization.FormatContentDependencyLoader(listing?.Name ?? loader.LoaderId, BoundsText(loader.MinVersion, loader.MaxVersion)!),
        };
    }

    /// <summary>
    /// The listings whose release in the saved channel has a required entry for
    /// <paramref name="modId"/>, most downloaded first. An any_of entry never
    /// counts, because it names no single mod and the listing can do without this one.
    /// </summary>
    private IEnumerable<DependencyMember> RequiredByMembers(string modId)
    {
        var instance = ActiveInstance;
        return MostDownloadedFirst(ListingsNaming(modId, ModDependencyKind.Required)).Select(item => DependentMember(item, instance));
    }

    /// <summary>The other listings whose release in the saved channel has an entry of this kind for <paramref name="modId"/>.</summary>
    private IEnumerable<DiscoverItem> ListingsNaming(string modId, ModDependencyKind kind) => _listings.Where(item => !ModIds.Equals(item.ModId, modId)
        && item.LatestInChannel?.Dependencies.Any(dependency => dependency.Kind == kind && ModIds.Equals(dependency.ModId, modId)) == true);

    /// <summary>
    /// A row names only the listing that needs this mod. A version range under its
    /// name would read as a version of that listing. The row shows a state only when
    /// the active instance holds the listing, because "Not installed" would be on nearly every row.
    /// </summary>
    private DependencyMember DependentMember(DiscoverItem item, InstanceItem? instance)
    {
        var installed = instance?.InstalledVersionOf(item.ModId);
        var state = installed is { } version ? Localization.FormatContentDependencyInstalled(version.ToString()) : null;
        return new DependencyMember(this, item.ModId, item, boundsText: null, state, installed is not null, isConflicting: false);
    }

    /// <summary>
    /// What <paramref name="release"/> names as a conflict, then the listings whose
    /// release in the saved channel names this release as one, most downloaded first.
    /// The resolver reports a conflict only within the bounds of the entry, so a listing
    /// outside them has no row. Without a release there is no version to compare, so
    /// every listing that names <paramref name="modId"/> has a row. A mod has one row.
    /// A conflict entry is never an any_of entry.
    /// </summary>
    internal IReadOnlyList<DependencyMember> BuildConflictMembers(string modId, ModVersionMetadata? release)
    {
        var instance = ActiveInstance;
        var inactive = InactiveForItem(release, instance);
        var named = (release?.Dependencies ?? []).Where(dependency => dependency.Kind == ModDependencyKind.Conflict)
            .GroupBy(conflict => conflict.ModId!, ModIds.Comparer)
            .Select(entries => NamedConflictMember(entries.Key, entries.ToList(), instance, inactive));
        var naming = MostDownloadedFirst(ListingsNaming(modId, ModDependencyKind.Conflict).Where(item => release is null || NamesAsConflict(item, modId, release.Version)))
            .Select(item => ConflictingListingMember(item, release, instance));

        // a mod that this release names keeps its place, and the row of the listing replaces it, because the conflict then holds for the newest release of that mod, whatever the range of the named entry says
        return named.Concat(naming)
            .GroupBy(member => member.ModId, ModIds.Comparer)
            .Select(rows => rows.Last())
            .ToList();
    }

    /// <summary>One row for all the entries that a release has for one mod. An entry without bounds covers every version, so the row then shows no range.</summary>
    private DependencyMember NamedConflictMember(string modId, IReadOnlyList<ModDependency> entries, InstanceItem? instance, IReadOnlySet<string>? inactive)
    {
        var installed = instance?.InstalledVersionOf(modId);
        var conflicting = installed is { } version && entries.Any(entry => entry.BoundsContain(version));
        var ranges = entries.Select(entry => BoundsText(entry.MinVersion, entry.MaxVersion)).ToList();
        var boundsText = ranges.Contains(null) ? null : string.Join(", ", ranges.Distinct());
        return new DependencyMember(this, modId, IndexListing(modId), boundsText, NamedStateText(modId, instance, installed, inactive), installed is not null, conflicting);
    }

    private static bool NamesAsConflict(DiscoverItem item, string modId, ModVersion version) => item.LatestInChannel!.Dependencies
        .Any(dependency => dependency.Kind == ModDependencyKind.Conflict && ModIds.Equals(dependency.ModId, modId) && dependency.BoundsContain(version));

    /// <summary>
    /// A row for a listing that names this mod as a conflict. The bounds of that entry
    /// are versions of this mod, so they do not show under the name of the listing. A
    /// held listing is marked only when a release exists, because without one no bounds were compared.
    /// </summary>
    private DependencyMember ConflictingListingMember(DiscoverItem item, ModVersionMetadata? release, InstanceItem? instance)
    {
        var installed = instance?.InstalledVersionOf(item.ModId);
        return new DependencyMember(this, item.ModId, item, boundsText: null, InstallStateText(instance, installed), installed is not null, installed is not null && release is not null);
    }

    internal DiscoverItem? IndexListing(string modId) => _listings.FirstOrDefault(item => item.Source == "index" && ModIds.Equals(item.ModId, modId));

    internal string? BoundsText(ModVersion? min, ModVersion? max) => (min, max) switch
    {
        (null, null) => null,
        ({ } low, null) => Localization.FormatContentDependencyMin(low.ToString()),
        (null, { } high) => Localization.FormatContentDependencyMax(high.ToString()),
        ({ } low, { } high) => Localization.FormatContentDependencyRange(low.ToString(), high.ToString()),
    };
}

public enum DependencyGroupKind
{
    Required,
    AnyOf,
    Recommended,
    Suggested,
    Optional,
    Other,
}

/// <summary>One kind of dependency on the Dependencies tab, with a hint on what it means for the player.</summary>
public sealed record DependencyGroup(DependencyGroupKind Kind, string Heading, string Hint, IReadOnlyList<DependencyEntry> Entries);

/// <summary>One dependency entry. An any_of entry lists its members as alternatives, labeled outside the Any of group.</summary>
public sealed record DependencyEntry(bool IsAnyOf, bool ShowsOneOf, IReadOnlyList<DependencyMember> Members);

/// <summary>One mod on the Dependencies tab, with its state in the active instance.</summary>
public sealed partial class DependencyMember
{
    private readonly MainViewModel _owner;
    private readonly DiscoverItem? _listing;
    private readonly string? _title;

    public string ModId { get; }

    public string Name => _listing?.Name ?? ModId;

    /// <summary>The text of the row, the name unless set.</summary>
    public string Title
    {
        get => _title ?? Name;
        init => _title = value;
    }

    public ListingImage? Icon => _listing?.Icon;

    public bool IsListed => _listing is not null;

    public string? BoundsText { get; }

    /// <summary>
    /// Installed with its version, or not installed. Null when no instance is
    /// active, and on a Required by row whose listing the instance does not hold.
    /// </summary>
    public string? StateText { get; }

    public bool IsInstalled { get; }

    public string? StateTip => IsInstalled ? _owner.InstalledInText : null;

    /// <summary>An installed mod on the Conflicts with card, when the bounds of the conflict entry contain the version that the entry is about.</summary>
    public bool IsConflicting { get; }

    public bool IsInstalledWithoutConflict => IsInstalled && !IsConflicting;

    public DependencyMember(MainViewModel owner, string modId, DiscoverItem? listing, string? boundsText, string? stateText, bool isInstalled, bool isConflicting)
    {
        _owner = owner;
        _listing = listing;
        ModId = modId;
        BoundsText = boundsText;
        StateText = stateText;
        IsInstalled = isInstalled;
        IsConflicting = isConflicting;
    }

    [RelayCommand]
    private Task OpenAsync() => _listing is null ? Task.CompletedTask : _owner.OpenContentAsync(_listing);
}
