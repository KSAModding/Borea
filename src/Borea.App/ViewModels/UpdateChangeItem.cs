using System;
using System.Collections.Generic;
using System.Linq;
using Borea.App.Localization;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>One mod that a plan of "Update all" changes, with the version it replaces, or none for a mod that the plan adds.</summary>
public sealed partial class UpdateChangeItem : ObservableObject
{
    private readonly LocalizationService _localization;

    /// <param name="changelogs">The changelogs of the releases of this mod, newest first.</param>
    internal UpdateChangeItem(LocalizationService localization, string modId, string name, ModVersion? fromVersion, ModVersion toVersion, IReadOnlyList<ReleaseChangelog> changelogs)
    {
        _localization = localization;
        ModId = modId;
        Name = name;
        FromVersion = fromVersion?.ToString();
        ToVersion = toVersion.ToString();
        IsOlder = fromVersion is { } from && toVersion < from;

        // one release needs no title of its own, because the row above it names the version, and a heading of the author can name it too
        Changelogs = changelogs.Select(changelog => changelog with { Title = changelogs.Count > 1 && !OpensWithItsVersion(changelog) ? changelog.Version ?? changelog.Title : string.Empty }).ToList();
    }

    private static bool OpensWithItsVersion(ReleaseChangelog changelog)
    {
        var firstLine = changelog.Text?.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.TrimStart();
        return firstLine is not null && changelog.Version is { } version && firstLine.StartsWith('#') && firstLine.Contains(version, StringComparison.Ordinal);
    }

    internal string ModId { get; }

    public string Name { get; }

    public string? FromVersion { get; }

    public string ToVersion { get; }

    public bool IsNew => FromVersion is null;

    public bool IsOlder { get; }

    internal IReadOnlyList<ReleaseChangelog> Changelogs { get; }

    /// <summary>The only entry when it is a link, which the row shows itself, because a fold that reveals one link is a click too many.</summary>
    public ContentLink? Link => Changelogs is [{ Text: null, Link: { } link }] ? link : null;

    public bool CanExpand => Changelogs.Count > 0 && Link is null;

    /// <summary>An update without a changelog says so. A mod that the plan adds or moves back never has one, because only the releases that an update passes are read.</summary>
    public bool HasNoNotes => !IsNew && !IsOlder && Changelogs.Count == 0;

    /// <summary>The row itself while it folds, so that only the button body is built.</summary>
    public UpdateChangeItem? FoldingRow => CanExpand ? this : null;

    /// <summary>The row itself while it does not fold, so that only the plain body is built.</summary>
    public UpdateChangeItem? PlainRow => CanExpand ? null : this;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownChangelogs))]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    private bool _isExpanded;

    /// <summary>Empty while folded, so that a folded row builds no text and a long list opens quickly.</summary>
    public IReadOnlyList<ReleaseChangelog> ShownChangelogs => IsExpanded ? Changelogs : [];

    public string ToggleText => IsExpanded ? _localization.FormatUpdateAllHideChanges(Name) : _localization.FormatUpdateAllShowChanges(Name);

    public string VersionChangeText => FromVersion is null ? ToVersion : _localization.FormatUpdateAllVersionChange(FromVersion, ToVersion);

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}
