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
/// needs, grouped by kind (RFC 0031), and the listings that need this mod.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<DependencyGroup> ContentDependencyGroups { get; } = [];

    /// <summary>The loader the release in the header needs, or null when it needs none.</summary>
    public DependencyMember? ContentLoaderDependency { get; private set; }

    /// <summary>The index listings whose newest release in the saved channel needs this mod.</summary>
    public ObservableCollection<DependencyMember> ContentRequiredBy { get; } = [];

    public bool HasContentRequiredBy => ContentRequiredBy.Count > 0;

    public string? ContentDependenciesHeading => LatestVersion is { } release ? Localization.FormatContentDependenciesOf(release.Version) : null;

    public string? ContentDependenciesEmptyText => LatestVersion is null
        ? Localization.ContentDependenciesNoRelease
        : ContentDependencyGroups.Count == 0 ? Localization.ContentNoDependencies : null;

    /// <summary>
    /// A loader page shows the tab only when its release has dependencies or a
    /// listing names the loader in a required entry. The loader that a release
    /// runs on does not count.
    /// </summary>
    public bool HasContentDependenciesTab => !IsLoaderContent || ContentDependencyGroups.Count > 0 || HasContentRequiredBy;

    [RelayCommand]
    private void ShowContentDependencies() => ContentTab = ContentPageTab.Dependencies;

    // a page without a release does not change LatestVersion, so the listings that need it follow the page here
    partial void OnSelectedContentChanged(DiscoverItem? value) => RefreshContentDependencies();

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
        ContentRequiredBy.Clear();
        if (SelectedContent is { } content)
        {
            foreach (var member in RequiredByMembers(content.ModId))
                ContentRequiredBy.Add(member);
        }

        if (IsDependenciesTab && !HasContentDependenciesTab)
            ContentTab = ContentPageTab.Description;

        OnPropertyChanged(nameof(ContentLoaderDependency));
        OnPropertyChanged(nameof(ContentDependenciesHeading));
        OnPropertyChanged(nameof(ContentDependenciesEmptyText));
        OnPropertyChanged(nameof(HasContentRequiredBy));
        OnPropertyChanged(nameof(HasContentDependenciesTab));
    }

    /// <summary>The groups in the order of #392, each only when it has entries.</summary>
    internal IReadOnlyList<DependencyGroup> BuildDependencyGroups(ModVersionMetadata release)
    {
        var instance = ActiveInstance;
        return Enum.GetValues<DependencyGroupKind>()
            .Select(kind => new DependencyGroup(
                kind,
                GroupHeading(kind),
                GroupHint(kind),
                release.Dependencies.Where(dependency => GroupOf(dependency) == kind).Select(dependency => Entry(dependency, instance)).ToList()))
            .Where(group => group.Entries.Count > 0)
            .ToList();
    }

    private static DependencyGroupKind GroupOf(ModDependency dependency) => dependency.Kind switch
    {
        ModDependencyKind.Required => dependency.IsAnyOf ? DependencyGroupKind.AnyOf : DependencyGroupKind.Required,
        ModDependencyKind.Conflict => DependencyGroupKind.Conflicts,
        ModDependencyKind.Recommends => DependencyGroupKind.Recommended,
        ModDependencyKind.Suggests => DependencyGroupKind.Suggested,
        ModDependencyKind.Optional => DependencyGroupKind.Optional,
        _ => DependencyGroupKind.Other,
    };

    private string GroupHeading(DependencyGroupKind kind) => kind switch
    {
        DependencyGroupKind.Required => Localization.ContentDependencyRequired,
        DependencyGroupKind.AnyOf => Localization.ContentDependencyAnyOf,
        DependencyGroupKind.Conflicts => Localization.ContentDependencyConflicts,
        DependencyGroupKind.Recommended => Localization.ContentDependencyRecommended,
        DependencyGroupKind.Suggested => Localization.ContentDependencySuggested,
        DependencyGroupKind.Optional => Localization.ContentDependencyOptional,
        _ => Localization.ContentDependencyOther,
    };

    private string GroupHint(DependencyGroupKind kind) => kind switch
    {
        DependencyGroupKind.Required => Localization.ContentDependencyRequiredHint,
        DependencyGroupKind.AnyOf => Localization.ContentDependencyAnyOfHint,
        DependencyGroupKind.Conflicts => Localization.ContentDependencyConflictsHint,
        DependencyGroupKind.Recommended => Localization.ContentDependencyRecommendedHint,
        DependencyGroupKind.Suggested => Localization.ContentDependencySuggestedHint,
        DependencyGroupKind.Optional => Localization.ContentDependencyOptionalHint,
        _ => Localization.ContentDependencyOtherHint,
    };

    private DependencyEntry Entry(ModDependency dependency, InstanceItem? instance) => dependency.IsAnyOf
        ? new DependencyEntry(true, GroupOf(dependency) != DependencyGroupKind.AnyOf, dependency.AnyOf.Select(member => Member(member.ModId, member.MinVersion, member.MaxVersion, instance)).ToList())
        : new DependencyEntry(false, false, [Member(dependency.ModId, dependency.MinVersion, dependency.MaxVersion, instance, dependency.Kind == ModDependencyKind.Conflict ? dependency : null)]);

    private DependencyMember Member(string modId, ModVersion? min, ModVersion? max, InstanceItem? instance, ModDependency? conflict = null)
    {
        var installed = instance?.InstalledVersionOf(modId);
        var conflicting = installed is { } version && conflict is not null && conflict.BoundsContain(version);
        var state = instance is null ? null
            : installed is { } present ? Localization.FormatContentDependencyInstalled(present.ToString())
            : Localization.ContentDependencyNotInstalled;
        return new DependencyMember(this, modId, IndexListing(modId), BoundsText(min, max), state, installed is not null, conflicting);
    }

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
        var dependents = _listings.Where(item => !ModIds.Equals(item.ModId, modId)
            && item.LatestInChannel?.Dependencies.Any(dependency => dependency.Kind == ModDependencyKind.Required && ModIds.Equals(dependency.ModId, modId)) == true);
        return MostDownloadedFirst(dependents).Select(item => DependentMember(item, instance));
    }

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
    Conflicts,
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

    /// <summary>An installed mod that a conflict entry names, within the conflicting range.</summary>
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
