using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public enum ListingStep
{
    /// <summary>Choose a new listing from a release host, or a listed one to change.</summary>
    Start = 0,

    /// <summary>Edit the fields, with the checks and the file beside them.</summary>
    Form = 1,
}

/// <summary>
/// The "List your mod" page: a listing draft for content-index, checked while the author types,
/// and handed to GitHub as a pull request of one document.
/// </summary>
public sealed partial class ListingEditor : ObservableObject
{
    public static IReadOnlyList<string> DependencyKinds { get; } = ["required", "optional", "recommends", "suggests", "conflict"];

    public static IReadOnlyList<string> Authorities { get; } = ["github", "spacedock"];

    private static readonly string[] KnownLinks = ["forums", "homepage", "repository", "spacedock", "bugtracker", "discussions"];

    private readonly MainViewModel _owner;
    private ListingDraft _base = new();
    private string? _listedText;
    private ListingArchiveFacts? _archive;
    private ContentIndexSnapshot? _snapshot;
    private ListingSource? _source;
    private bool _loading;
    private Task _opening = Task.CompletedTask;
    private CancellationTokenSource? _busy;
    private CancellationTokenSource? _tagProposal;

    public ListingEditor(MainViewModel owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _owner.Localization.PropertyChanged += (_, _) => RefreshText();
        _owner.PropertyChanged += OnOwnerChanged;
    }

    private LocalizationService Localization => _owner.Localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartStep), nameof(IsFormStep))]
    private ListingStep _step;

    public bool IsStartStep => Step == ListingStep.Start;

    public bool IsFormStep => Step == ListingStep.Form;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadSourceCommand))]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isReadingSource;

    [ObservableProperty]
    private string? _sourceProgress;

    [ObservableProperty]
    private string? _sourceError;

    public ObservableCollection<string> ListedIds { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadListedCommand))]
    private string? _selectedListedId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isLoadingListed;

    [ObservableProperty]
    private string? _listedError;

    public bool IsBusy => IsReadingSource || IsLoadingListed;

    /// <summary>How long the forums link has to stay unchanged before its thread prefixes are read.</summary>
    internal TimeSpan ForumsDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    /// <summary>The last reading of the forums thread for tags, which tests wait for.</summary>
    internal Task TagProposal { get; private set; } = Task.CompletedTask;

    /// <summary>What Borea read from the latest release archive, or null.</summary>
    [ObservableProperty]
    private string? _archiveText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew), nameof(PullRequestText), nameof(CanUseLoader))]
    private bool _isEdit;

    public bool IsNew => !IsEdit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseLoader))]
    private string _type = ListingDraft.ModType;

    /// <summary>A mod-loader carries no [loader].</summary>
    public bool CanUseLoader => Type == ListingDraft.ModType;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _authors = string.Empty;

    [ObservableProperty]
    private string _abstract = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _license = string.Empty;

    [ObservableProperty]
    private string _forums = string.Empty;

    [ObservableProperty]
    private string _homepage = string.Empty;

    [ObservableProperty]
    private string _repository = string.Empty;

    [ObservableProperty]
    private string _spaceDockPage = string.Empty;

    [ObservableProperty]
    private string _bugTracker = string.Empty;

    [ObservableProperty]
    private string _discussions = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsAuthority))]
    private string _releasesGitHub = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsAuthority))]
    private string _releasesSpaceDock = string.Empty;

    [ObservableProperty]
    private string? _releasesAuthority;

    public bool NeedsAuthority => ReleasesGitHub.Trim().Length > 0 && ReleasesSpaceDock.Trim().Length > 0;

    [ObservableProperty]
    private string _gameMin = string.Empty;

    [ObservableProperty]
    private string _gameMax = string.Empty;

    [ObservableProperty]
    private bool _usesLoader;

    [ObservableProperty]
    private string _loaderId = ListingPrefill.StarMapId;

    [ObservableProperty]
    private string _loaderMin = string.Empty;

    [ObservableProperty]
    private string _loaderMax = string.Empty;

    [ObservableProperty]
    private string _freeTags = string.Empty;

    [ObservableProperty]
    private bool _isDeprecated;

    [ObservableProperty]
    private string _supersededBy = string.Empty;

    public ObservableCollection<ListingTagChip> CuratedTags { get; } = [];

    public ObservableCollection<ListingDependencyRow> Dependencies { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(CanAddIcon))]
    private ListingImageRow? _icon;

    public bool HasIcon => Icon is not null;

    public bool CanAddIcon => Icon is null;

    public ObservableCollection<ListingImageRow> DescriptionImages { get; } = [];

    public bool CanAddDescriptionImage => DescriptionImages.Count < ContentImages.MaxDescriptionImages;

    public ObservableCollection<ListingIssue> Errors { get; } = [];

    public ObservableCollection<ListingIssue> Notes { get; } = [];

    public bool HasErrors => Errors.Count > 0;

    public bool HasNotes => Notes.Count > 0;

    public bool HasNoIssues => Errors.Count == 0 && Notes.Count == 0;

    [ObservableProperty]
    private string? _schemaText;

    /// <summary>The draft as the fields describe it now.</summary>
    [ObservableProperty]
    private ListingDraft _draft = new();

    /// <summary>The listing file the draft gives, in the layout of content-index.</summary>
    [ObservableProperty]
    private string _documentText = string.Empty;

    /// <summary>What happened after the last action on the file.</summary>
    [ObservableProperty]
    private string? _outputMessage;

    /// <summary>The page the last pull request action opened.</summary>
    internal ListingPullRequestPage? LastPullRequestPage { get; private set; }

    public string PullRequestText => IsEdit ? Localization.ListingEditPullRequestText : Localization.ListingNewPullRequestText;

    public bool CanOpenPullRequest => !HasErrors && ModIds.IsValid(Draft.Id);

    /// <summary>Loads the content index and the newest schema, which the checks use. The draft stays as it is.</summary>
    internal Task OpenAsync()
    {
        Enter();
        return _opening = OpenCoreAsync();
    }

    private async Task OpenCoreAsync()
    {
        if (_owner.Services is not { } services)
            return;

        try
        {
            _snapshot = await services.IndexSnapshots.GetSnapshotAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            _snapshot = null;
        }

        var listed = _snapshot?.Listings
            .Where(listing => listing.Authored?.Type is ContentType.Mod or ContentType.ModLoader)
            .Select(listing => listing.Id)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        MainViewModel.Arrange(ListedIds, listed);
        FillCuratedTags(Draft.Tags);

        if (services.ListingValidator.SchemaOrigin != ListingSchemaOrigin.Downloaded)
        {
            try
            {
                await services.ListingValidator.LoadSchemaAsync();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
            {
            }
        }

        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanReadSource))]
    private async Task ReadSourceAsync()
    {
        if (_owner.Services is not { } services || IsBusy)
            return;

        if (!ListingSourceReference.TryParse(SourceText, out var reference))
        {
            SourceError = Localization.ListingSourceInvalid;
            return;
        }

        SourceError = null;
        SourceProgress = Localization.ListingReading;
        IsReadingSource = true;
        using var cancel = new CancellationTokenSource();
        _busy = cancel;
        try
        {
            await _opening;
            var progress = new Progress<DownloadProgress>(value =>
            {
                if (IsReadingSource)
                    SourceProgress = Localization.FormatListingDownloading(SizeText(value.BytesDownloaded));
            });
            var source = await services.ListingSources.ReadAsync(reference, progress, cancel.Token);
            var draft = ListingPrefill.Apply(NewDraft(), source, _snapshot, services.InstalledVersion.GetInstalledVersion()?.Version);
            if (draft.LinkOf("forums") is { } forums && _snapshot is { } snapshot)
            {
                var prefixes = await services.ForumThreads.GetPrefixesAsync(forums, cancel.Token);
                draft = draft with { Tags = ListingPrefill.TagsFor(prefixes, snapshot.Tags) };
            }

            cancel.Token.ThrowIfCancellationRequested();
            _source = source;
            _archive = source.Archive;
            ArchiveText = DescribeArchive(source);
            Load(draft);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (ListingSourceException exception)
        {
            SourceError = exception.Message;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException)
        {
            SourceError = Localization.FormatListingReadFailed(exception.Message);
        }
        finally
        {
            _busy = null;
            IsReadingSource = false;
            SourceProgress = null;
        }
    }

    private bool CanReadSource() => SourceText.Trim().Length > 0;

    [RelayCommand]
    private void StartEmpty()
    {
        _source = null;
        _archive = null;
        ArchiveText = null;
        Load(NewDraft());
    }

    [RelayCommand(CanExecute = nameof(CanLoadListed))]
    private async Task LoadListedAsync()
    {
        if (_owner.Services is not { } services || SelectedListedId is not { } id || IsBusy)
            return;

        ListedError = null;
        IsLoadingListed = true;
        using var cancel = new CancellationTokenSource();
        _busy = cancel;
        try
        {
            var text = await services.ListedDocuments.GetListingAsync(id, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            var draft = ListingDraft.FromDocument(services.ListingFormat.Read(text));
            _source = null;
            _archive = null;
            ArchiveText = null;
            Load(draft, text);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or FormatException)
        {
            ListedError = Localization.FormatListingLoadFailed(exception.Message);
        }
        finally
        {
            _busy = null;
            IsLoadingListed = false;
        }
    }

    private bool CanLoadListed() => SelectedListedId is not null;

    /// <summary>Stops reading a source or a listed file, and drops what it would have loaded.</summary>
    [RelayCommand]
    internal void Cancel()
    {
        _busy?.Cancel();
        _tagProposal?.Cancel();
    }

    /// <summary>Drops the draft and goes back to the choice of a source.</summary>
    [RelayCommand]
    private void StartOver()
    {
        Cancel();
        ForgetPullRequest();
        OutputMessage = null;
        SourceError = null;
        ListedError = null;
        Step = ListingStep.Start;
    }

    [RelayCommand]
    private void AddDependency()
    {
        Dependencies.Add(new ListingDependencyRow(this, new ListingDependency(string.Empty, DependencyKinds[0])));
        Refresh();
    }

    [RelayCommand]
    private void AddIcon() => Icon = new ListingImageRow(this, ListingImageRole.Icon, new ListingImageRecord(string.Empty));

    [RelayCommand]
    private void AddDescriptionImage()
    {
        if (!CanAddDescriptionImage)
            return;

        DescriptionImages.Add(new ListingImageRow(this, ListingImageRole.Description, new ListingImageRecord(string.Empty)));
        OnPropertyChanged(nameof(CanAddDescriptionImage));
        Refresh();
    }

    internal void Remove(ListingDependencyRow row)
    {
        Dependencies.Remove(row);
        Refresh();
    }

    internal void Remove(ListingImageRow row)
    {
        if (ReferenceEquals(row, Icon))
        {
            Icon = null;
        }
        else if (DescriptionImages.Remove(row))
        {
            OnPropertyChanged(nameof(CanAddDescriptionImage));
            Refresh();
        }
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (_owner.WindowServices is not { } window)
            return;

        await window.CopyTextAsync(DocumentText);
        _owner.ShowSuccessToast(() => Localization.ListingCopied);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_owner.WindowServices is not { } window)
            return;

        try
        {
            var fileName = await window.SaveTextFileAsync(Localization.ListingSaveAs, $"{(Draft.Id.Length > 0 ? Draft.Id : "listing")}.toml", Localization.ListingFileType, DocumentText);
            if (fileName is not null)
                _owner.ShowSuccessToast(() => Localization.FormatListingSaved(fileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _owner.ShowErrorToast(() => Localization.ListingSaveFailed, exception.Message);
        }
    }

    /// <summary>
    /// Opens the GitHub page that starts the pull request: the new-file page with the file filled in for a new
    /// listing, or the edit page of a listed one. When the page does not carry the file, the file is copied to paste.
    /// </summary>
    [RelayCommand]
    private async Task OpenPullRequestAsync()
    {
        if (!CanOpenPullRequest)
        {
            OutputMessage = Localization.ListingFixErrors;
            return;
        }

        var page = IsEdit
            ? ListingPullRequestLinks.Edit(_base.Id)
            : ListingPullRequestLinks.NewFile(Draft.Id, DocumentText);
        var error = _owner.OpenListingPage(page.Url.AbsoluteUri);
        if (error is not null && page.CarriesText)
        {
            page = ListingPullRequestLinks.NewFileWithoutText(Draft.Id);
            error = _owner.OpenListingPage(page.Url.AbsoluteUri);
        }

        if ((error is not null || !page.CarriesText) && _owner.WindowServices is { } window)
            await window.CopyTextAsync(DocumentText);

        LastPullRequestPage = page;
        OutputMessage = error is not null
            ? Localization.FormatListingOpenFailed(error)
            : page.CarriesText ? Localization.ListingOpenedWithText : Localization.ListingOpenedPaste;
    }

    internal async Task PickImageFileAsync(ListingImageRow row)
    {
        if (_owner.WindowServices is not { } window)
            return;

        var file = await window.OpenImageFileAsync(Localization.ListingChooseFile, Localization.ListingImageFileType, ListingImageMeasurement.MaxBytes(row.Role));
        if (file is not null)
            row.Apply(ListingImageMeasurement.Of(file.Bytes, row.Role));
    }

    internal async Task MeasureImageAsync(ListingImageRow row)
    {
        if (_owner.Services is not { } services)
            return;

        row.IsMeasuring = true;
        try
        {
            row.Apply(await services.ListingImages.MeasureAsync(row.Url.Trim(), row.Role));
        }
        finally
        {
            row.IsMeasuring = false;
        }
    }

    private ListingDraft NewDraft() => new()
    {
        GameMin = ListingPrefill.DefaultGameMin(_owner.Services?.InstalledVersion.GetInstalledVersion()?.Version, _snapshot) ?? string.Empty,
    };

    private void Load(ListingDraft draft, string? listedText = null)
    {
        _tagProposal?.Cancel();
        _loading = true;
        try
        {
            _base = draft;
            _listedText = listedText;
            IsEdit = draft.IsEdit;
            Type = draft.Type;
            Id = draft.Id;
            Name = draft.Name;
            Authors = string.Join(", ", draft.Authors);
            Abstract = draft.Abstract;
            Description = draft.Description ?? string.Empty;
            License = draft.License;
            Forums = draft.LinkOf("forums") ?? string.Empty;
            Homepage = draft.LinkOf("homepage") ?? string.Empty;
            Repository = draft.LinkOf("repository") ?? string.Empty;
            SpaceDockPage = draft.LinkOf("spacedock") ?? string.Empty;
            BugTracker = draft.LinkOf("bugtracker") ?? string.Empty;
            Discussions = draft.LinkOf("discussions") ?? string.Empty;
            ReleasesGitHub = draft.Releases?.GitHub ?? string.Empty;
            ReleasesSpaceDock = draft.Releases?.SpaceDock?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            ReleasesAuthority = draft.Releases?.Authority;
            GameMin = draft.GameMin;
            GameMax = draft.GameMax ?? string.Empty;
            UsesLoader = draft.Loader is not null;
            LoaderId = draft.Loader?.Id ?? ListingPrefill.StarMapId;
            LoaderMin = draft.Loader?.Min ?? ListingPrefill.NewestStableVersion(_snapshot, ListingPrefill.StarMapId) ?? string.Empty;
            LoaderMax = draft.Loader?.Max ?? string.Empty;
            IsDeprecated = draft.Status == "deprecated";
            SupersededBy = draft.SupersededBy ?? string.Empty;
            FillCuratedTags(draft.Tags);
            var curated = CuratedTags.Select(chip => chip.Tag).ToHashSet(StringComparer.Ordinal);
            FreeTags = string.Join(", ", draft.Tags.Where(tag => !curated.Contains(tag)));

            Dependencies.Clear();
            foreach (var dependency in draft.Dependencies)
                Dependencies.Add(new ListingDependencyRow(this, dependency));

            Icon = draft.Icon is { } icon ? new ListingImageRow(this, ListingImageRole.Icon, icon) : null;
            DescriptionImages.Clear();
            foreach (var image in draft.DescriptionImages)
                DescriptionImages.Add(new ListingImageRow(this, ListingImageRole.Description, image));
            OnPropertyChanged(nameof(CanAddDescriptionImage));
        }
        finally
        {
            _loading = false;
        }

        OutputMessage = null;
        ForgetPullRequest();
        Step = ListingStep.Form;
        Refresh();
    }

    private void FillCuratedTags(IReadOnlyList<string> selected)
    {
        var vocabulary = _snapshot?.Tags.GetTags(ContentType.Mod) ?? [];
        var chosen = CuratedTags.Where(chip => chip.IsSelected).Select(chip => chip.Tag).Concat(selected).ToHashSet(StringComparer.Ordinal);
        CuratedTags.Clear();
        foreach (var tag in vocabulary)
            CuratedTags.Add(new ListingTagChip(this, tag.Tag, _owner.CategoryName(tag.Tag, tag.Name), tag.Meaning, chosen.Contains(tag.Tag)));
    }

    /// <summary>Builds the draft from the fields, writes the file and runs the checks.</summary>
    internal void Refresh()
    {
        if (_loading)
            return;

        var draft = BuildDraft(out var pageIssues);
        Draft = draft;
        if (_owner.Services is not { } services)
            return;

        var document = draft.ToDocument();
        DocumentText = services.ListingFormat.Write(document, _listedText);
        var result = services.ListingValidator.Validate(document, new ListingCheckContext(_snapshot, IsEdit ? _base.Id : null, _archive));
        var issues = pageIssues.Concat(result.Issues).Distinct().ToList();

        MainViewModel.Arrange(Errors, issues.Where(issue => issue.Severity == ListingIssueSeverity.Error).ToList());
        MainViewModel.Arrange(Notes, issues.Where(issue => issue.Severity == ListingIssueSeverity.Note).ToList());
        SchemaText = services.ListingValidator.SchemaOrigin switch
        {
            ListingSchemaOrigin.Downloaded => Localization.ListingSchemaDownloaded,
            ListingSchemaOrigin.Cached => Localization.ListingSchemaCached,
            _ => Localization.ListingSchemaEmbedded,
        };
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasNoIssues));
        OnPropertyChanged(nameof(CanOpenPullRequest));
        RefreshOverview(issues);
        OnPropertyChanged(nameof(CanPublish));
        ScheduleOwnershipCheck();
    }

    internal string ImageFacts(long? width, long? height, long? size) => Localization.FormatListingImageFacts(
        width?.ToString(CultureInfo.CurrentCulture) ?? string.Empty,
        height?.ToString(CultureInfo.CurrentCulture) ?? string.Empty,
        size?.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty);

    internal string ImageNotMeasured => Localization.ListingImageNotMeasured;

    internal string PreservedDependencyText(string ids) => Localization.FormatListingDependencyPreserved(ids);

    /// <summary>The texts that follow the display language.</summary>
    private void RefreshText()
    {
        if (_source is not null)
            ArchiveText = DescribeArchive(_source);
        Icon?.RefreshText();
        foreach (var image in DescriptionImages)
            image.RefreshText();
        // a chip keeps the name it was built with, so the tags are built again in the new language
        if (CuratedTags.Count > 0)
            FillCuratedTags([]);
        OnPropertyChanged(nameof(PullRequestText));
        RefreshPullRequestText();
        Refresh();
    }

    private ListingDraft BuildDraft(out List<ListingIssue> pageIssues)
    {
        pageIssues = [];
        var links = new List<ListingLink>();
        foreach (var (key, value) in new[] { ("forums", Forums), ("homepage", Homepage), ("repository", Repository), ("spacedock", SpaceDockPage), ("bugtracker", BugTracker), ("discussions", Discussions) })
        {
            if (value.Trim().Length > 0)
                links.Add(new ListingLink(key, value.Trim()));
        }

        links.AddRange(_base.Links.Where(link => !KnownLinks.Contains(link.Key, StringComparer.Ordinal)));

        long? spaceDock = null;
        if (ReleasesSpaceDock.Trim().Length > 0)
        {
            if (long.TryParse(ReleasesSpaceDock.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                spaceDock = number;
            else
                pageIssues.Add(new ListingIssue(ListingIssueSeverity.Error, "releases.spacedock", Localization.FormatListingSpaceDockNotNumber(ReleasesSpaceDock.Trim())));
        }

        var releases = ReleasesGitHub.Trim().Length > 0 || spaceDock is not null
            ? new ListingReleases(Empty(ReleasesGitHub), spaceDock, NeedsAuthority ? ReleasesAuthority : null)
            : null;

        var free = FreeTags.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = CuratedTags.Where(chip => chip.IsSelected).Select(chip => chip.Tag).Concat(free).Distinct(StringComparer.Ordinal).ToList();

        return _base with
        {
            Type = Type,
            Id = Id.Trim(),
            Name = Name.Trim(),
            Authors = Authors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Abstract = Abstract.Trim(),
            Description = Description.Length == 0 ? null : Description.Replace("\r\n", "\n", StringComparison.Ordinal),
            License = License.Trim(),
            Tags = tags,
            Status = IsDeprecated ? "deprecated" : _base.Status is null ? null : "active",
            SupersededBy = IsDeprecated ? Empty(SupersededBy) : null,
            Releases = releases,
            Links = links,
            GameMin = GameMin.Trim(),
            GameMax = Empty(GameMax),
            Loader = CanUseLoader && UsesLoader ? new ListingLoader(LoaderId.Trim(), LoaderMin.Trim(), Empty(LoaderMax)) : null,
            Dependencies = Dependencies.Select(row => row.ToDependency()).ToList(),
            Icon = Icon?.ToRecord(),
            DescriptionImages = DescriptionImages.Select(row => row.ToRecord()).ToList(),
        };
    }

    private static string? Empty(string value) => value.Trim().Length == 0 ? null : value.Trim();

    private string DescribeArchive(ListingSource source)
    {
        if (source.ArchiveProblem is { } problem)
            return Localization.FormatListingArchiveProblem(problem);
        if (source.Host.Latest is not { } latest)
            return Localization.ListingNoRelease;
        if (source.Archive is not { } archive)
            return Localization.ListingNoRelease;
        if (archive.Root is not { } root)
            return Localization.FormatListingArchiveNoRoot(latest.Tag);

        var text = Localization.FormatListingArchiveRoot(latest.Tag, root);
        return archive.IsCodeMod ? $"{text} {Localization.FormatListingArchiveCodeMod(archive.EntryAssembly ?? root)}" : text;
    }

    private static string SizeText(long bytes) => bytes >= 1024 * 1024
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} MB", bytes / (1024.0 * 1024.0))
        : string.Format(CultureInfo.CurrentCulture, "{0:0} KB", bytes / 1024.0);

    partial void OnIdChanged(string value) => Refresh();

    partial void OnNameChanged(string value) => Refresh();

    partial void OnAuthorsChanged(string value) => Refresh();

    partial void OnAbstractChanged(string value) => Refresh();

    partial void OnDescriptionChanged(string value) => Refresh();

    partial void OnLicenseChanged(string value) => Refresh();

    partial void OnForumsChanged(string value)
    {
        Refresh();
        if (_loading)
            return;

        _tagProposal?.Cancel();
        _tagProposal = null;
        if (ForumsThreadLink.ThreadOf(value.Trim()) is not null && !CuratedTags.Any(chip => chip.IsSelected))
        {
            _tagProposal = new CancellationTokenSource();
            TagProposal = ProposeTagsAsync(value.Trim(), _tagProposal.Token);
        }
    }

    /// <summary>Selects the curated tags that the prefixes of the forums thread map to, once the author stops typing.</summary>
    private async Task ProposeTagsAsync(string forums, CancellationToken cancellationToken)
    {
        if (_owner.Services is not { } services || _snapshot is not { } snapshot)
            return;

        try
        {
            await Task.Delay(ForumsDelay, cancellationToken);
            var prefixes = await services.ForumThreads.GetPrefixesAsync(forums, cancellationToken);
            if (cancellationToken.IsCancellationRequested || CuratedTags.Any(chip => chip.IsSelected))
                return;

            var tags = ListingPrefill.TagsFor(prefixes, snapshot.Tags).ToHashSet(StringComparer.Ordinal);
            foreach (var chip in CuratedTags.Where(chip => tags.Contains(chip.Tag)))
                chip.IsSelected = true;
        }
        catch (OperationCanceledException)
        {
        }
    }

    partial void OnHomepageChanged(string value) => Refresh();

    partial void OnRepositoryChanged(string value) => Refresh();

    partial void OnSpaceDockPageChanged(string value) => Refresh();

    partial void OnBugTrackerChanged(string value) => Refresh();

    partial void OnDiscussionsChanged(string value) => Refresh();

    partial void OnReleasesGitHubChanged(string value) => Refresh();

    partial void OnReleasesSpaceDockChanged(string value) => Refresh();

    partial void OnReleasesAuthorityChanged(string? value) => Refresh();

    partial void OnGameMinChanged(string value) => Refresh();

    partial void OnGameMaxChanged(string value) => Refresh();

    partial void OnUsesLoaderChanged(bool value) => Refresh();

    partial void OnLoaderIdChanged(string value) => Refresh();

    partial void OnLoaderMinChanged(string value) => Refresh();

    partial void OnLoaderMaxChanged(string value) => Refresh();

    partial void OnFreeTagsChanged(string value) => Refresh();

    partial void OnIsDeprecatedChanged(bool value) => Refresh();

    partial void OnSupersededByChanged(string value) => Refresh();

    partial void OnIconChanged(ListingImageRow? value) => Refresh();
}
