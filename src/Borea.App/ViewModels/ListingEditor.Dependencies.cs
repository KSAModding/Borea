using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The help for the dependencies of a mod: what its mod.toml already declares, which the index derives by itself, a search
/// over the listed mods and loaders for the id, and the stamped releases of the named mod for the bounds. A loader goes into
/// [loader], because the index keeps the bounds of a loader there and refuses a dependency on it.
/// </summary>
public sealed partial class ListingEditor
{
    private IReadOnlyList<ContentIndexListing> _dependencyCandidates = [];

    /// <summary>What the archive of the author's release declares, read on request or with the source. It wins over the snapshot.</summary>
    private ListingDeclaredDependencies? _declaredFromArchive;

    /// <summary>The facts of the archive that "Read from my archive" read, which the checks then use as the stamper would.</summary>
    private ListingArchiveFacts? _readArchive;

    private CancellationTokenSource? _archiveRead;

    public ObservableCollection<ListingDeclaredDependencyRow> DeclaredDependencies { get; } = [];

    /// <summary>Which release the declared dependencies come from, or null when Borea read none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeclaredHelp))]
    private string? _declaredText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadArchiveDependenciesCommand))]
    private bool _isReadingArchive;

    /// <summary>The progress of reading the archive, or why it gave no mod.toml to read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeclaredHelp))]
    private string? _archiveReadText;

    /// <summary>
    /// Whether the card shows what the mod.toml declares. Only a mod has dependencies that the index derives from its
    /// mod.toml, because the stamper reads no mod.toml for any other type.
    /// </summary>
    public bool HasDeclaredHelp => CanUseLoader && (DeclaredText is not null || HasReleasesHost || ArchiveReadText is not null);

    [ObservableProperty]
    private string _dependencyQuery = string.Empty;

    /// <summary>The listed mods and loaders without an entry yet whose id, name or authors contain the query.</summary>
    public ObservableCollection<ListedListing> DependencyMatches { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddListedDependencyCommand))]
    private ListedListing? _selectedDependencyMatch;

    public bool HasNoDependencyMatch => DependencyQuery.Trim().Length > 0 && DependencyMatches.Count == 0 && _dependencyCandidates.Count > 0;

    /// <summary>Says that a picked loader went into [loader] under Compatibility, or null.</summary>
    [ObservableProperty]
    private string? _loaderSetText;

    /// <summary>
    /// Downloads the archive of the newest release of the [releases] host of the form, and shows the dependencies that the
    /// mod.toml at its install root declares. Nothing is downloaded before the author asks for it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReadArchiveDependencies))]
    private async Task ReadArchiveDependenciesAsync()
    {
        if (_owner.Services is not { } services || !CanUseLoader)
            return;

        if (ReleasesSource() is not { } reference)
        {
            ArchiveReadText = Localization.ListingDeclaredNeedsHost;
            return;
        }

        ArchiveReadText = Localization.ListingReading;
        IsReadingArchive = true;
        using var cancel = new CancellationTokenSource();
        _archiveRead = cancel;
        try
        {
            var progress = new Progress<DownloadProgress>(value =>
            {
                if (IsReadingArchive && !cancel.IsCancellationRequested)
                    ArchiveReadText = Localization.FormatListingDownloading(SizeText(value.BytesDownloaded));
            });
            var installRoot = AuthoredInstallRoot();
            var source = await services.ListingSources.ReadAsync(reference, installRoot, progress, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            if (source.Archive is { } archive && source.Host.Latest is { } latest)
            {
                _readArchive = archive;
                ShowArchive(latest.Tag, archive, installRoot is not null);
                Refresh();
            }
            else
            {
                ArchiveReadText = source.ArchiveProblem is { } problem ? Localization.FormatListingArchiveProblem(problem) : Localization.ListingNoRelease;
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            ArchiveReadText = null;
        }
        catch (ListingSourceException exception)
        {
            ArchiveReadText = exception.Message;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException)
        {
            ArchiveReadText = Localization.FormatListingReadFailed(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_archiveRead, cancel))
                _archiveRead = null;
            IsReadingArchive = false;
        }
    }

    private bool CanReadArchiveDependencies() => !IsReadingArchive;

    /// <summary>Stops the download that "Read from my archive" started. The rest of the form stays as it is.</summary>
    [RelayCommand]
    private void CancelArchiveRead() => _archiveRead?.Cancel();

    /// <summary>The release host that decides which releases exist, by the rule of the ownership check, or null.</summary>
    private ListingSourceReference? ReleasesSource() =>
        Draft.Releases is not null && ListingAuthority.Of(Draft) is { } host && ListingSourceReference.TryParse(host.Target, out var reference) ? reference : null;

    /// <summary>The install root that the listing authors in [install], which the stamper then reads the mod.toml from.</summary>
    private string? AuthoredInstallRoot() => _base.Original?.GetTable("install")?.GetString("root");

    /// <summary>Shows what the mod.toml of a read archive declares, or why the stamper would read no mod.toml in it.</summary>
    private void ShowArchive(string release, ListingArchiveFacts archive, bool authoredRoot)
    {
        if (archive.ModDependencies is { } dependencies)
        {
            ArchiveReadText = null;
            ShowDeclared(ListingDependencyChoices.FromModToml(release, dependencies));
            return;
        }

        ShowDeclared(null);
        ArchiveReadText = archive.Root is null && !authoredRoot
            ? Localization.FormatListingDeclaredNoRoot(release)
            : Localization.FormatListingDeclaredNoModToml(release);
    }

    /// <summary>Adds an entry for the chosen listed mod, or makes the chosen loader the loader.</summary>
    [RelayCommand(CanExecute = nameof(CanAddListedDependency))]
    private void AddListedDependency()
    {
        if (SelectedDependencyMatch?.Id is not { } id)
            return;

        AddDependencyEntry(id, ListingDependencyChoices.Required);
        DependencyQuery = string.Empty;
    }

    private bool CanAddListedDependency() => SelectedDependencyMatch is not null;

    /// <summary>
    /// Adds an editable entry with the id in the spelling the index lists it, so the index takes it for the derived entry of
    /// the same id. A listed loader becomes the loader instead.
    /// </summary>
    internal void AddDependencyEntry(string id, string kind)
    {
        var listing = _snapshot is { } snapshot ? ListingDependencyChoices.Listing(snapshot, id) : null;
        if (listing is not null && ListingDependencyChoices.IsLoader(listing) && CanUseLoader)
        {
            UseLoader(listing.Id);
            return;
        }

        LoaderSetText = null;
        Dependencies.Add(new ListingDependencyRow(this, new ListingDependency(listing?.Id ?? id, kind)));
        FindDependencies();
        Refresh();
    }

    /// <summary>Turns on [loader] with the loader, and takes its newest stable release as the oldest version when the loader changes.</summary>
    private void UseLoader(string id)
    {
        if (!ModIds.Equals(LoaderId.Trim(), id) || LoaderMin.Trim().Length == 0)
            LoaderMin = ListingPrefill.NewestStableVersion(_snapshot, id) ?? string.Empty;
        LoaderId = id;
        UsesLoader = true;
        LoaderSetText = Localization.FormatListingLoaderSet(id);
        FindDependencies();
        RefreshDependencyHelp();
    }

    /// <summary>Whether an entry of the form names the id, as its own id or as one of its alternatives.</summary>
    internal bool HasDependencyEntry(string id) => Dependencies.Any(row => row.NamedIds.Any(named => ModIds.Equals(named.Trim(), id)));

    /// <summary>Whether [loader] of the form names the id.</summary>
    internal bool IsLoaderSet(string id) => CanUseLoader && UsesLoader && ModIds.Equals(LoaderId.Trim(), id);

    /// <summary>Whether the snapshot lists the id as a loader that a client can install.</summary>
    internal bool IsListedLoader(string id) =>
        _snapshot is { } snapshot && ListingDependencyChoices.Listing(snapshot, id) is { } listing && ListingDependencyChoices.IsLoader(listing);

    /// <summary>The stamped releases of a listed mod, newest first and without the yanked ones, a release that is not stable marked with its status.</summary>
    internal IReadOnlyList<ListingReleaseChoice> DependencyVersions(string id)
    {
        if (_snapshot is not { } snapshot || ListingDependencyChoices.Listing(snapshot, id) is not { } listing)
            return [];

        return ListingDependencyChoices.Releases(listing)
            .Select(release => new ListingReleaseChoice(release.Version.ToString(), release.ReleaseStatus == ReleaseStatus.Stable ? string.Empty : _owner.ReleaseStatusText(release.ReleaseStatus)))
            .ToList();
    }

    internal string? NewestDependencyVersion(string id) =>
        _snapshot is { } snapshot && ListingDependencyChoices.Listing(snapshot, id) is { } listing ? ListingDependencyChoices.Newest(listing)?.Version.ToString() : null;

    internal string? DependencyKindText(string kind) => kind switch
    {
        "required" => Localization.ListingDependencyKindRequired,
        "optional" => Localization.ListingDependencyKindOptional,
        "recommends" => Localization.ListingDependencyKindRecommends,
        "suggests" => Localization.ListingDependencyKindSuggests,
        "conflict" => Localization.ListingDependencyKindConflict,
        _ => null,
    };

    internal string DeclaredDependencyText(ListingDeclaredDependency dependency) => dependency.Kind == ListingDependencyChoices.Optional
        ? Localization.FormatListingDeclaredOptional(dependency.Id)
        : Localization.FormatListingDeclaredRequired(dependency.Id);

    internal string ModLoaderText => Localization.ContentTypeModLoader;

    internal string NeedsNewestHint => Localization.ListingNeedsNewestHint;

    /// <summary>
    /// A note for each entry whose id no listing and no pack of the index holds, because no player can install that
    /// dependency. The checks already name an id that the index holds with another type, and a dependency on the listing itself.
    /// </summary>
    private IEnumerable<ListingIssue> DependencyIssues(IReadOnlyList<ListingDependency> dependencies)
    {
        if (_snapshot is not { } snapshot)
            yield break;

        var own = Id.Trim();
        for (var index = 0; index < dependencies.Count; index++)
        {
            var id = dependencies[index].Id;
            if (dependencies[index].Preserved is null && ModIds.IsValid(id) && !ModIds.Equals(id, own) && !ListingDependencyChoices.IsHeld(snapshot, id))
                yield return new ListingIssue(ListingIssueSeverity.Note, $"dependencies[{index}]", Localization.FormatListingDependencyNotListed(id));
        }
    }

    /// <summary>Puts the issues of each entry under its row, the same lines the list of problems shows.</summary>
    private void ShowDependencyIssues(IReadOnlyList<ListingIssue> shown)
    {
        for (var index = 0; index < Dependencies.Count; index++)
        {
            var place = $"dependencies[{index}]";
            var own = shown.Where(issue => issue.Location == place || issue.Location.StartsWith(place + ".", StringComparison.Ordinal)).ToList();
            Dependencies[index].ErrorText = Lines(own, ListingIssueSeverity.Error);
            Dependencies[index].NoteText = Lines(own, ListingIssueSeverity.Note);
        }

        static string? Lines(List<ListingIssue> issues, ListingIssueSeverity severity) =>
            issues.Where(issue => issue.Severity == severity).Select(issue => issue.Message).ToList() is { Count: > 0 } lines ? string.Join("\n", lines) : null;
    }

    /// <summary>
    /// Shows what the mod.toml declares: what Borea read from the archive, else the derived dependencies of the newest stamped
    /// release of the mod the draft changes. A new listing has no stamped release, and only a mod has derived dependencies.
    /// </summary>
    private void FillDeclared()
    {
        var declared = !CanUseLoader
            ? null
            : _declaredFromArchive ?? (HasFixedId && _snapshot is { } snapshot ? ListingDependencyChoices.Derived(snapshot, _base.Id) : null);
        MainViewModel.Arrange(DeclaredDependencies, declared?.Dependencies.Select(dependency => new ListingDeclaredDependencyRow(this, dependency)).ToList() ?? []);
        DeclaredText = declared is null
            ? null
            : declared.Dependencies.Count > 0 ? Localization.FormatListingDeclared(declared.Release) : Localization.FormatListingDeclaredNone(declared.Release);
    }

    private void ShowDeclared(ListingDeclaredDependencies? declared)
    {
        _declaredFromArchive = declared;
        FillDeclared();
    }

    /// <summary>Forgets what the archive declared and stops a reading of it, for a new draft or another release host.</summary>
    private void ForgetDeclared()
    {
        _archiveRead?.Cancel();
        _declaredFromArchive = null;
        _readArchive = null;
        ArchiveReadText = null;
        LoaderSetText = null;
    }

    /// <summary>What the archive of the old release host declared does not hold for the new one.</summary>
    private void ReleasesHostChanged()
    {
        if (_loading)
            return;

        ForgetDeclared();
        FillDeclared();
    }

    private void RefreshDependencyHelp()
    {
        foreach (var row in DeclaredDependencies)
            row.RefreshText();
        foreach (var row in Dependencies)
            row.RefreshText();
    }

    private void FindDependencies()
    {
        _dependencyCandidates = _snapshot is { } snapshot
            ? ListingDependencyChoices.Candidates(snapshot).Where(listing => CanUseLoader || !ListingDependencyChoices.IsLoader(listing)).ToList()
            : [];
        var query = DependencyQuery.Trim();
        var own = Id.Trim();
        var matches = query.Length == 0
            ? []
            : _dependencyCandidates
                .Where(listing => !ModIds.Equals(listing.Id, own) && !HasDependencyEntry(listing.Id) && !IsLoaderSet(listing.Id) && Matches(listing, query))
                .Select(listing => new ListedListing(listing.Id, listing.Authored!.Name, AuthorsText(listing.Authored.Authors), IsOwn: false, IsLoader: ListingDependencyChoices.IsLoader(listing)))
                .OrderBy(listing => listing.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

        MainViewModel.Arrange(DependencyMatches, matches);
        SelectedDependencyMatch = matches.FirstOrDefault(listing => listing.Id == SelectedDependencyMatch?.Id) ?? matches.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoDependencyMatch));
    }

    partial void OnDependencyQueryChanged(string value) => FindDependencies();
}
