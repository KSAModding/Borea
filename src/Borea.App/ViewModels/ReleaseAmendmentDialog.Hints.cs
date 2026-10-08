using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// The help of the amendment form: the versions that each field offers, the value that the selected releases state now, and
/// whether the typed value narrows or widens them. The preview stays the check, and every field still takes any text.
/// </summary>
public sealed partial class ReleaseAmendmentDialog
{
    private ContentIndexSnapshot? _snapshot;
    private ReleaseFiles? _files;
    private Dictionary<string, ReleaseFileValues?> _values = new(StringComparer.Ordinal);
    private GameVersionOption? _installed;
    private IReadOnlyList<ReleaseAmendmentChoice> _gameChoices = [];
    private IReadOnlyList<ReleaseAmendmentChoice> _loaderChoices = [];

    /// <summary>The game builds that Borea knows, newest first, the installed build and the value now marked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameVersions))]
    private IReadOnlyList<ReleaseAmendmentChoice> _gameMinChoices = [];

    [ObservableProperty]
    private IReadOnlyList<ReleaseAmendmentChoice> _gameMaxChoices = [];

    /// <summary>The stamped releases of the loader that the releases state, newest first, the value now marked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoaderVersions))]
    private IReadOnlyList<ReleaseAmendmentChoice> _loaderMinChoices = [];

    [ObservableProperty]
    private IReadOnlyList<ReleaseAmendmentChoice> _loaderMaxChoices = [];

    /// <summary>While the release files are read for their values now.</summary>
    [ObservableProperty]
    private bool _isReadingFiles;

    /// <summary>Whether the files are read but no release is selected, so no field can say what it states now.</summary>
    [ObservableProperty]
    private bool _needsSelectionForValues;

    [ObservableProperty]
    private ReleaseAmendmentHint _gameMinHint = ReleaseAmendmentHint.None;

    [ObservableProperty]
    private ReleaseAmendmentHint _gameMaxHint = ReleaseAmendmentHint.None;

    [ObservableProperty]
    private ReleaseAmendmentHint _loaderMinHint = ReleaseAmendmentHint.None;

    [ObservableProperty]
    private ReleaseAmendmentHint _loaderMaxHint = ReleaseAmendmentHint.None;

    /// <summary>Whether the game fields offer a list. Without one they are plain text fields.</summary>
    public bool HasGameVersions => GameMinChoices.Count > 0;

    public bool HasLoaderVersions => LoaderMinChoices.Count > 0;

    /// <summary>Reads the index snapshot for the version lists and the release files for their values. Without them the form works as typed.</summary>
    private async Task LoadHelpAsync(BoreaServices services)
    {
        try
        {
            _snapshot = await services.IndexSnapshots.GetSnapshotAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            _snapshot = null;
        }

        _installed = services.InstalledVersion.GetInstalledVersion() is { Version: { } build } installed ? new GameVersionOption(installed.RawVersion, build.Revision) : null;
        RefreshChoices();
        RefreshHints();

        IsReadingFiles = true;
        try
        {
            _files = await services.ReleaseAmendments.ReleaseFilesAsync(ListingId);
            _values = _files.Files.ToDictionary(file => file.Path, file => ReleaseFileValues.Read(file.Text), StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is StewardException or ReleaseAmendmentRefusedException)
        {
            _files = null;
        }
        finally
        {
            IsReadingFiles = false;
        }

        RefreshChoices();
        RefreshHints();
    }

    /// <summary>Takes the versions of the game and loader fields from the snapshot and the release files.</summary>
    private void RefreshChoices()
    {
        var builds = (_snapshot?.GameVersions?.Versions ?? []).Concat(_files?.GameVersions ?? []);
        var installed = _installed?.Revision;
        _gameChoices = [.. MainViewModel.GameVersionOptionsOf(_installed is { } own ? builds.Append(own.Text) : builds)
            .DistinctBy(option => option.Revision)
            .Select(option => new ReleaseAmendmentChoice(option.Text, option.Revision == installed ? _owner.Localization.DiscoverInstalled : string.Empty))];
        _loaderChoices = LoaderId() is { } loader ? ReleaseChoices(loader) : [];
    }

    /// <summary>Says for each field what the selected releases state now and what the typed value does to them, and marks the value now in each list.</summary>
    private void RefreshHints()
    {
        var files = SelectedFiles();
        NeedsSelectionForValues = _files is not null && files.Count == 0;
        Func<ReleaseFileValues, string?>? loaderMin = States(files, values => values.Loader is not null) ? values => values.Loader!.Min : null;
        Func<ReleaseFileValues, string?>? loaderMax = loaderMin is null ? null : values => values.Loader!.Max;
        GameMinHint = Hint(files, values => values.GameMin, Typed(GameMin) is { } gameMin ? new ReleaseChange { GameMin = gameMin } : null);
        GameMaxHint = Hint(files, values => values.GameMax, !RemoveGameMax && Typed(GameMax) is { } gameMax ? new ReleaseChange { GameMax = gameMax } : null);
        LoaderMinHint = Hint(files, loaderMin, !RemoveLoaderMin && Typed(LoaderMin) is { } typedLoaderMin ? new ReleaseChange { LoaderMin = typedLoaderMin } : null);
        LoaderMaxHint = Hint(files, loaderMax, !RemoveLoaderMax && Typed(LoaderMax) is { } typedLoaderMax ? new ReleaseChange { LoaderMax = typedLoaderMax } : null);
        GameMinChoices = Marked(_gameChoices, Current(files, values => values.GameMin), GameMinChoices);
        GameMaxChoices = Marked(_gameChoices, Current(files, values => values.GameMax), GameMaxChoices);
        LoaderMinChoices = Marked(_loaderChoices, Current(files, loaderMin), LoaderMinChoices);
        LoaderMaxChoices = Marked(_loaderChoices, Current(files, loaderMax), LoaderMaxChoices);
        foreach (var row in Dependencies)
            row.Refresh(files);
    }

    /// <summary>The value now, or null for none, and the effect of the change, which <see cref="ReleaseAmendment.EffectOf"/> finds with the checks of the preview.</summary>
    internal ReleaseAmendmentHint Hint(IReadOnlyList<ReleaseFile> files, Func<ReleaseFileValues, string?>? value, ReleaseChange? change) =>
        HintWithNow(files, value is null ? null : NowText(files, value, _owner.Localization.FormatStewardAmendNow), change);

    internal ReleaseAmendmentHint HintWithNow(IReadOnlyList<ReleaseFile> files, string? now, ReleaseChange? change)
    {
        if (change is null || files.Count == 0 || _files is null)
            return new ReleaseAmendmentHint(now, null, NeedsAuthor: false);

        var localization = _owner.Localization;
        return ReleaseAmendment.EffectOf(change, files, _files.GameVersions, DateTimeOffset.UtcNow) switch
        {
            ReleaseChangeEffect.Unchanged => new ReleaseAmendmentHint(now, localization.StewardAmendSame, NeedsAuthor: false),
            ReleaseChangeEffect.Narrows => new ReleaseAmendmentHint(now, localization.StewardAmendNarrows, NeedsAuthor: false),
            ReleaseChangeEffect.Widens when OnBehalfOfAuthor => new ReleaseAmendmentHint(now, localization.StewardAmendWidensOnBehalf, NeedsAuthor: false),
            ReleaseChangeEffect.Widens => new ReleaseAmendmentHint(now, localization.StewardAmendWidens, NeedsAuthor: true),
            _ => new ReleaseAmendmentHint(now, null, NeedsAuthor: false),
        };
    }

    /// <summary>
    /// "Now: 0.4.5", or each value with the releases that state it when they differ, such as "2026.9.7.5402 (1.2.0, 1.1.0); not set (1.0.0)".
    /// </summary>
    internal string? NowText(IReadOnlyList<ReleaseFile> files, Func<ReleaseFileValues, string?> value, Func<string, string> format)
    {
        if (files.Count == 0)
            return null;

        var localization = _owner.Localization;
        var groups = files.GroupBy(file => _values.GetValueOrDefault(file.Path) is { } read ? value(read) : null, StringComparer.Ordinal).ToList();
        return groups.Count == 1
            ? format(groups[0].Key ?? localization.StewardAmendNotSet)
            : localization.FormatStewardAmendNowDiffers(string.Join("; ", groups.Select(group =>
                $"{group.Key ?? localization.StewardAmendNotSet} ({string.Join(", ", group.Select(file => file.Version))})")));
    }

    /// <summary>The value that every selected release states, or null when they differ or state none.</summary>
    internal string? Current(IReadOnlyList<ReleaseFile> files, Func<ReleaseFileValues, string?>? value)
    {
        if (value is null || files.Count == 0)
            return null;

        var values = files.Select(file => _values.GetValueOrDefault(file.Path) is { } read ? value(read) : null).Distinct(StringComparer.Ordinal).ToList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>Whether every selected release was read and has the entry, such as a loader. A missing entry has no value now that a field could change.</summary>
    internal bool States(IReadOnlyList<ReleaseFile> files, Func<ReleaseFileValues, bool> has) =>
        files.Count > 0 && files.All(file => _values.GetValueOrDefault(file.Path) is { } read && has(read));

    /// <summary>The choices with the value now marked. A list that stays the same is kept, so an open list stays as it is.</summary>
    internal IReadOnlyList<ReleaseAmendmentChoice> Marked(IReadOnlyList<ReleaseAmendmentChoice> choices, string? now, IReadOnlyList<ReleaseAmendmentChoice> shown)
    {
        var mark = _owner.Localization.StewardAmendNowMark;
        IReadOnlyList<ReleaseAmendmentChoice> marked = [.. choices.Select(choice => choice with
        {
            NowMark = now is not null && string.Equals(choice.Value, now, StringComparison.Ordinal) ? mark : string.Empty,
        })];
        return marked.SequenceEqual(shown) ? shown : marked;
    }

    /// <summary>The read release files that the scope selects, in the order of the selection, or none while nothing is selected.</summary>
    private IReadOnlyList<ReleaseFile> SelectedFiles()
    {
        if (_files is not { } read || !HasSelection)
            return [];

        try
        {
            return [.. ReleaseAmendment.Select(read.Files.Select(file => file.Version), Selection)
                .Select(version => read.Files.First(file => file.Version == version))];
        }
        catch (ReleaseAmendmentRefusedException)
        {
            return [];
        }
    }

    /// <summary>The values of the selected release files, or of every file while nothing is selected.</summary>
    private IEnumerable<ReleaseFileValues> SelectedValues()
    {
        var files = SelectedFiles();
        return (files.Count > 0 ? files : _files?.Files ?? []).Select(file => _values.GetValueOrDefault(file.Path)).OfType<ReleaseFileValues>();
    }

    /// <summary>The loader that any release of the listing states, or else the one that the snapshot gives the listing.</summary>
    private string? LoaderId() =>
        (_files?.Files ?? []).Select(file => _values.GetValueOrDefault(file.Path)?.Loader?.Id).FirstOrDefault(id => id is not null)
        ?? _snapshot?.Listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, ListingId))?.Releases.Select(release => release.Loader?.LoaderId).FirstOrDefault(id => id is not null);

    /// <summary>The stamped releases of a listing in the snapshot, newest first and without the yanked ones, a release that is not stable marked with its status.</summary>
    internal IReadOnlyList<ReleaseAmendmentChoice> ReleaseChoices(string id) =>
        id.Length > 0 && _snapshot?.Listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, id)) is { } listing
            ? [.. listing.Releases.Where(release => !release.Yanked).OrderByDescending(release => release.Version)
                .Select(release => new ReleaseAmendmentChoice(release.Version.ToString(), release.ReleaseStatus == ReleaseStatus.Stable ? string.Empty : _owner.ReleaseStatusText(release.ReleaseStatus)))]
            : [];

    /// <summary>
    /// The ids that a dependency row offers: for a stated dependency first those that the selected releases state, with their kind, and
    /// then the listed mods whose id or name contains the typed text. A typed text that is one of the ids offers them all, so the list
    /// still shows the other ids after a pick.
    /// </summary>
    internal IReadOnlyList<ReleaseAmendmentChoice> IdChoices(bool stated, string typed)
    {
        var statedIds = stated
            ? SelectedValues().SelectMany(values => values.Dependencies).DistinctBy(dependency => dependency.Id, ModIds.Comparer)
                .Select(dependency => new ReleaseAmendmentChoice(dependency.Id, _owner.Localization.FormatStewardAmendStated(dependency.Kind)))
                .ToList()
            : [];
        var listed = (_snapshot?.Listings ?? [])
            .Where(listing => listing.Authored is { Type: ContentType.Mod } && listing.IndexStatus?.State != IndexStatusState.Delisted && !ModIds.Equals(listing.Id, ListingId))
            .Where(listing => !statedIds.Any(choice => ModIds.Equals(choice.Value, listing.Id)))
            .OrderBy(listing => listing.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var query = typed.Trim();
        if (statedIds.Any(choice => ModIds.Equals(choice.Value, query)) || listed.Any(listing => ModIds.Equals(listing.Id, query)))
            query = string.Empty;

        return [.. statedIds, .. listed
            .Where(listing => query.Length == 0 || listing.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || listing.Authored!.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(listing => new ReleaseAmendmentChoice(listing.Id, ModIds.Equals(listing.Authored!.Name, listing.Id) ? string.Empty : listing.Authored.Name))];
    }
}

/// <summary>One value that a field offers. An editable list writes <see cref="Value"/> into its field when the steward picks it.</summary>
/// <param name="Note">A short mark next to the value, such as the release status, "Installed" or the name of a mod, or empty.</param>
/// <param name="NowMark">"Now" when every selected release states this value now, or empty.</param>
public sealed record ReleaseAmendmentChoice(string Value, string Note, string NowMark = "")
{
    public bool HasNote => Note.Length > 0;

    public bool IsNow => NowMark.Length > 0;

    public override string ToString() => Value;
}

/// <summary>What a field of the form says under it.</summary>
/// <param name="Now">What the selected releases state now, or null before they are read or while nothing is selected.</param>
/// <param name="Effect">Whether the typed value narrows or widens the releases, or null without a value the checks can read.</param>
/// <param name="NeedsAuthor">Whether the value widens the releases without "On behalf of the author", which the preview then refuses.</param>
public sealed record ReleaseAmendmentHint(string? Now, string? Effect, bool NeedsAuthor)
{
    public static ReleaseAmendmentHint None { get; } = new(null, null, NeedsAuthor: false);

    public bool HasNow => Now is not null;

    public bool HasEffect => Effect is not null;

    public bool IsShown => HasNow || HasEffect;
}
