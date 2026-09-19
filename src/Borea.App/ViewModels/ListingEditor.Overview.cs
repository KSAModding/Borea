using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Borea.Core.Listings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

public enum ListingStepState
{
    Done = 0,
    ToDo = 1,
    Fix = 2,
    Optional = 3,
}

/// <summary>One section of the form, as the step list in the side panel shows it.</summary>
public sealed class ListingStepItem(string key, string name, ListingStepState state, string detail)
{
    /// <summary>The Tag of the section card on the page.</summary>
    public string Key { get; } = key;

    public string Name { get; } = name;

    public ListingStepState State { get; } = state;

    public string Detail { get; } = detail;

    public bool IsDone => State == ListingStepState.Done;

    public bool IsFix => State == ListingStepState.Fix;

    public bool IsOpen => State is ListingStepState.ToDo or ListingStepState.Optional;
}

/// <summary>
/// The side panel: a preview card in the style of a Discover row, the steps with their state, and the problems.
/// A required field that is still empty, and an image or dependency row that is still empty, count as missing
/// instead of as an error, so a new listing does not start with a list of errors.
/// </summary>
public sealed partial class ListingEditor
{
    private static readonly Regex RequiredProperty = new("'([^']+)' is a required property", RegexOptions.CultureInvariant);

    public ObservableCollection<ListingStepItem> Steps { get; } = [];

    public ObservableCollection<string> VisibleErrors { get; } = [];

    public ObservableCollection<string> VisibleNotes { get; } = [];

    [ObservableProperty]
    private string? _missingText;

    public bool HasVisibleErrors => VisibleErrors.Count > 0;

    public bool HasVisibleNotes => VisibleNotes.Count > 0;

    public bool IsComplete => MissingText is null && VisibleErrors.Count == 0 && VisibleNotes.Count == 0;

    public string PreviewName => Name.Trim().Length > 0 ? Name.Trim() : Id.Trim().Length > 0 ? Id.Trim() : Localization.ListingPreviewName;

    public string? PreviewAuthors => Authors.Trim().Length > 0 ? Localization.FormatContentByAuthor(Authors.Trim()) : null;

    public string PreviewAbstract => Abstract.Trim().Length > 0 ? Abstract.Trim() : Localization.ListingPreviewAbstract;

    public IReadOnlyList<string> PreviewTags =>
        CuratedTags.Where(chip => chip.IsSelected).Select(chip => chip.Name)
            .Concat(FreeTags.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

    private void RefreshOverview(IReadOnlyList<ListingIssue> issues)
    {
        // A location covers that key and everything below it, and the names are the required properties that a schema message may name for it.
        var missing = new List<(string Section, string Label, string Location, string[] Names)>();
        void Need(string section, string label, string value, string location, params string[] names)
        {
            if (value.Trim().Length == 0)
                missing.Add((section, label, location, names));
        }

        Need("about", Localization.ListingId, Id, "id", "id");
        Need("about", Localization.ListingName, Name, "name", "name");
        Need("about", Localization.ListingAuthors, Authors, "authors", "authors");
        Need("about", Localization.ListingAbstract, Abstract, "abstract", "abstract");
        Need("about", Localization.ListingLicense, License, "license", "license");
        Need("links", Localization.LinkForum, Forums, "links.forums", "forums", "links");
        Need("compatibility", Localization.ListingGameMin, GameMin, "compatibility.game_min", "game_min", "compatibility");
        if (Icon is { } icon && IsEmpty(icon))
            missing.Add(("images", Localization.ListingIcon, "images.icon", []));
        for (var index = 0; index < DescriptionImages.Count; index++)
        {
            if (IsEmpty(DescriptionImages[index]))
                missing.Add(("images", Localization.FormatListingDescriptionImageNumber(index + 1), $"images.description[{index}]", []));
        }

        for (var index = 0; index < Dependencies.Count; index++)
        {
            if (Dependencies[index].IsEditable && Dependencies[index].Id.Trim().Length == 0)
                missing.Add(("dependencies", Localization.FormatListingDependencyNumber(index + 1), $"dependencies[{index}]", []));
        }

        var locations = missing.Select(entry => entry.Location).ToList();
        var names = missing.SelectMany(entry => entry.Names).ToHashSet();
        var explained = issues.Where(issue => issue.Message.Contains("SPDX license list", StringComparison.Ordinal) || issue.Message.Contains("does not parse as an SPDX", StringComparison.Ordinal))
            .Select(issue => issue.Location).ToHashSet();
        var shown = issues
            .Where(issue => !IsCovered(issue, locations, names))
            .Where(issue => !(explained.Contains(issue.Location) && issue.Message.Contains("is not an SPDX license expression", StringComparison.Ordinal)))
            .ToList();

        MainViewModel.Arrange(VisibleErrors, shown.Where(issue => issue.Severity == ListingIssueSeverity.Error).Select(Line).ToList());
        MainViewModel.Arrange(VisibleNotes, shown.Where(issue => issue.Severity == ListingIssueSeverity.Note).Select(Line).ToList());
        MissingText = missing.Count == 0 ? null : Localization.FormatListingStillMissing(string.Join(", ", missing.Select(entry => entry.Label)));

        var fixes = shown.Where(issue => issue.Severity == ListingIssueSeverity.Error).GroupBy(SectionOf).ToDictionary(group => group.Key, group => group.Count());
        var open = missing.Select(entry => entry.Section).ToHashSet();
        var steps = new List<ListingStepItem>
        {
            Step("about", Localization.ListingAbout, Filled(Id, Name, Authors, Abstract, Description, License)),
            Step("links", Localization.ListingLinks, Filled(Forums, Homepage, Repository, SpaceDockPage, BugTracker, Discussions)),
            Step("releases", Localization.ListingReleases, Filled(ReleasesGitHub, ReleasesSpaceDock), recommended: true),
            Step("compatibility", Localization.ListingCompatibility, Filled(GameMin, GameMax) || UsesLoader),
            Step("dependencies", Localization.ListingDependencies, Dependencies.Count > 0),
            Step("tags", Localization.ListingTags, PreviewTags.Count > 0),
            Step("images", Localization.ListingImages, Icon is not null || DescriptionImages.Count > 0),
        };
        if (IsEdit)
            steps.Add(Step("status", Localization.ListingStatus, IsDeprecated));
        MainViewModel.Arrange(Steps, steps);

        OnPropertyChanged(nameof(HasVisibleErrors));
        OnPropertyChanged(nameof(HasVisibleNotes));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(PreviewName));
        OnPropertyChanged(nameof(PreviewAuthors));
        OnPropertyChanged(nameof(PreviewAbstract));
        OnPropertyChanged(nameof(PreviewTags));

        ListingStepItem Step(string key, string name, bool filled, bool recommended = false)
        {
            if (fixes.TryGetValue(key, out var count))
                return new ListingStepItem(key, name, ListingStepState.Fix, Localization.FormatListingStepFix(count));
            if (open.Contains(key))
                return new ListingStepItem(key, name, ListingStepState.ToDo, Localization.ListingStepToDo);
            if (filled)
                return new ListingStepItem(key, name, ListingStepState.Done, Localization.ListingStepDone);
            return new ListingStepItem(key, name, ListingStepState.Optional, recommended ? Localization.ListingStepRecommended : Localization.ListingStepOptional);
        }
    }

    private static bool IsEmpty(ListingImageRow row) => row.Url.Trim().Length == 0 && !row.IsMeasured;

    private static bool Filled(params string[] values) => values.Any(value => value.Trim().Length > 0);

    private static bool IsCovered(ListingIssue issue, List<string> locations, HashSet<string> names)
    {
        if (locations.Any(location => issue.Location == location || issue.Location.StartsWith(location + ".", StringComparison.Ordinal) || issue.Location.StartsWith(location + "[", StringComparison.Ordinal)))
            return true;

        return issue.Location is "" or "links" or "compatibility"
            && RequiredNames(issue.Message) is { Count: > 0 } required
            && required.All(names.Contains);
    }

    /// <summary>The properties a message names as required, or null when the message says anything else too.</summary>
    private static List<string>? RequiredNames(string message)
    {
        var matches = RequiredProperty.Matches(message);
        return matches.Count > 0 && matches.Count == message.Split("; ").Length ? matches.Select(match => match.Groups[1].Value).ToList() : null;
    }

    private static string SectionOf(ListingIssue issue)
    {
        var location = issue.Location.Length > 0 ? issue.Location : RequiredNames(issue.Message)?.FirstOrDefault() ?? string.Empty;
        return location.Split('.', '[')[0] switch
        {
            "links" => "links",
            "releases" => "releases",
            "compatibility" or "loader" or "game_min" or "game_max" => "compatibility",
            "dependencies" => "dependencies",
            "tags" => "tags",
            "images" => "images",
            "status" or "superseded_by" => "status",
            _ => "about",
        };
    }

    private string Line(ListingIssue issue) => LabelOf(issue.Location) is { } label ? $"{label}: {issue.Message}" : issue.Message;

    private string? LabelOf(string location)
    {
        if (location.StartsWith("images.icon", StringComparison.Ordinal))
            return Localization.ListingIcon;
        if (Number(location, "images.description[") is { } image)
            return Localization.FormatListingDescriptionImageNumber(image + 1);
        if (Number(location, "dependencies[") is { } dependency)
            return Localization.FormatListingDependencyNumber(dependency + 1);

        return location switch
        {
            "" => null,
            "id" => Localization.ListingId,
            "name" => Localization.ListingName,
            "authors" => Localization.ListingAuthors,
            "abstract" => Localization.ListingAbstract,
            "description" => Localization.ListingDescription,
            "license" => Localization.ListingLicense,
            "links" => Localization.ListingLinks,
            "links.forums" => Localization.LinkForum,
            "links.homepage" => Localization.LinkHomepage,
            "links.repository" => Localization.LinkRepository,
            "links.spacedock" => "SpaceDock",
            "links.bugtracker" => Localization.LinkBugTracker,
            "links.discussions" => Localization.LinkDiscussions,
            "releases" => Localization.ListingReleases,
            "releases.github" => Localization.ListingReleasesGitHub,
            "releases.spacedock" => Localization.ListingReleasesSpaceDock,
            "releases.authority" => Localization.ListingReleasesAuthority,
            "compatibility" => Localization.ListingCompatibility,
            "compatibility.game_min" => Localization.ListingGameMin,
            "compatibility.game_max" => Localization.ListingGameMax,
            "loader" or "loader.id" => Localization.ListingLoaderId,
            "loader.min" => Localization.ListingLoaderMin,
            "loader.max" => Localization.ListingLoaderMax,
            "tags" => Localization.ListingTags,
            "status" => Localization.ListingStatus,
            "superseded_by" => Localization.ListingSupersededBy,
            _ => location,
        };
    }

    private static int? Number(string location, string prefix)
    {
        if (!location.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var end = location.IndexOf(']', prefix.Length);
        return end > prefix.Length && int.TryParse(location.AsSpan(prefix.Length, end - prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}
