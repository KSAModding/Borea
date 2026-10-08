using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public enum ReleaseAmendmentScope
{
    Checked,
    UpTo,
    All,
}

/// <summary>
/// The steward amendment of the releases of one listing: the releases, the change and the reason. It shows the change per release file
/// before anything is written, and opens the pull request of the preview once. Any edit drops the preview, so what is sent is what was shown.
/// </summary>
public sealed partial class ReleaseAmendmentDialog : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _run = Task.CompletedTask;

    public ReleaseAmendmentDialog(MainViewModel owner, string listingId)
    {
        _owner = owner;
        ListingId = listingId;
        Platforms = [.. ReleaseChange.Platforms.Select(platform => new ReleaseAmendmentPlatform(this, platform))];
    }

    public string ListingId { get; }

    public LocalizationService Localization => _owner.Localization;

    public string Title => _owner.Localization.FormatStewardAmendTitle(ListingId);

    /// <summary>The stamped releases on the base branch, newest first.</summary>
    public ObservableCollection<ReleaseAmendmentVersion> Versions { get; } = [];

    public ObservableCollection<ReleaseAmendmentDependencyRow> Dependencies { get; } = [];

    /// <summary>The platforms that a change of os can name, on the author's behalf only.</summary>
    public IReadOnlyList<ReleaseAmendmentPlatform> Platforms { get; }

    /// <summary>The changed files of the preview, and the ones that already say this.</summary>
    public ObservableCollection<ReleaseAmendmentFile> Files { get; } = [];

    public IReadOnlyList<string> VersionNames => [.. Versions.Select(version => version.Version)];

    public bool HasVersions => Versions.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScopeChecked))]
    [NotifyPropertyChangedFor(nameof(IsScopeUpTo))]
    [NotifyPropertyChangedFor(nameof(IsScopeAll))]
    private ReleaseAmendmentScope _scope;

    [ObservableProperty]
    private string? _upTo;

    [ObservableProperty]
    private string _gameMin = string.Empty;

    [ObservableProperty]
    private string _gameMax = string.Empty;

    [ObservableProperty]
    private string _loaderMin = string.Empty;

    [ObservableProperty]
    private string _loaderMax = string.Empty;

    [ObservableProperty]
    private bool _yank;

    /// <summary>Whether the steward amends on the author's request, which may also widen the releases (RFC 0079).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameMinLabel))]
    [NotifyPropertyChangedFor(nameof(GameMaxLabel))]
    [NotifyPropertyChangedFor(nameof(LoaderMinLabel))]
    [NotifyPropertyChangedFor(nameof(LoaderMaxLabel))]
    [NotifyPropertyChangedFor(nameof(DependenciesHint))]
    [NotifyPropertyChangedFor(nameof(BoundDependencyLabel))]
    [NotifyPropertyChangedFor(nameof(InvalidAuthorRequestText))]
    private bool _onBehalfOfAuthor;

    /// <summary>The link to the author's request, which the pull request names.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InvalidAuthorRequestText))]
    private string _authorRequest = string.Empty;

    // The changes below widen a release, so the form offers them on the author's behalf only and clears them when that ends.
    [ObservableProperty]
    private bool _removeGameMax;

    [ObservableProperty]
    private bool _unyank;

    [ObservableProperty]
    private bool _changeOs;

    [ObservableProperty]
    private bool _removeLoaderMin;

    [ObservableProperty]
    private bool _removeLoaderMax;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InvalidReasonText))]
    private string _reason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isPreviewing;

    /// <summary>While the pull request is being opened, the dialog cannot close, so the steward sees how it ended.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isOpening;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(NothingText))]
    [NotifyPropertyChangedFor(nameof(MentionText))]
    private ReleaseAmendmentPreview? _preview;

    /// <summary>Why tools/amend.py or the checks would refuse the amendment.</summary>
    [ObservableProperty]
    private string? _refusal;

    /// <summary>The words of the checks behind <see cref="Refusal"/>, in English as the checks write them.</summary>
    [ObservableProperty]
    private string? _refusalDetails;

    /// <summary>Says that the files changed on the base branch since the preview, which now shows them.</summary>
    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpened))]
    [NotifyPropertyChangedFor(nameof(OpenedText))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private ReleaseAmendmentPullRequest? _opened;

    public bool IsScopeChecked
    {
        get => Scope == ReleaseAmendmentScope.Checked;
        set => SetScope(value, ReleaseAmendmentScope.Checked);
    }

    public bool IsScopeUpTo
    {
        get => Scope == ReleaseAmendmentScope.UpTo;
        set => SetScope(value, ReleaseAmendmentScope.UpTo);
    }

    public bool IsScopeAll
    {
        get => Scope == ReleaseAmendmentScope.All;
        set => SetScope(value, ReleaseAmendmentScope.All);
    }

    public bool HasPreview => Preview is not null;

    public bool CanEdit => !IsPreviewing && !IsOpening && Opened is null;

    /// <summary>A preview needs the releases, a selection, a change, a reason that fits on one line, and on the author's behalf the link to the request.</summary>
    public bool CanPreview => CanEdit && !IsLoading && Versions.Count > 0 && Preview is null && HasSelection && HasChange && IndexStatusChange.IsValidReason(Reason)
        && (!OnBehalfOfAuthor || ReleaseAmendmentRequest.IsValidAuthorRequest(AuthorRequest.Trim()));

    public string GameMinLabel => OnBehalfOfAuthor ? _owner.Localization.StewardAmendGameMinAuthor : _owner.Localization.StewardAmendGameMin;

    public string GameMaxLabel => OnBehalfOfAuthor ? _owner.Localization.StewardAmendGameMaxAuthor : _owner.Localization.StewardAmendGameMax;

    public string LoaderMinLabel => OnBehalfOfAuthor ? _owner.Localization.StewardAmendLoaderMinAuthor : _owner.Localization.StewardAmendLoaderMin;

    public string LoaderMaxLabel => OnBehalfOfAuthor ? _owner.Localization.StewardAmendLoaderMaxAuthor : _owner.Localization.StewardAmendLoaderMax;

    public string DependenciesHint => OnBehalfOfAuthor ? _owner.Localization.StewardAmendDependenciesHintAuthor : _owner.Localization.StewardAmendDependenciesHint;

    public string BoundDependencyLabel => OnBehalfOfAuthor ? _owner.Localization.StewardAmendBoundDependencyAuthor : _owner.Localization.StewardAmendBoundDependency;

    public string? InvalidAuthorRequestText =>
        OnBehalfOfAuthor && AuthorRequest.Trim().Length > 0 && !ReleaseAmendmentRequest.IsValidAuthorRequest(AuthorRequest.Trim())
            ? _owner.Localization.StewardAmendInvalidAuthorRequest
            : null;

    public bool CanOpen => CanEdit && Preview is { Changed.Count: > 0 };

    public bool CanClose => !IsOpening;

    /// <summary>Reading the releases failed and can run again.</summary>
    public bool CanRetry => Error is not null && Versions.Count == 0 && !IsLoading;

    public bool IsOpened => Opened is not null;

    public string? InvalidReasonText =>
        Reason.Trim().Length > 0 && !IndexStatusChange.IsValidReason(Reason) ? _owner.Localization.StewardRefusedInvalidReason : null;

    public string? NothingText => Preview is { Changed.Count: 0 } ? _owner.Localization.StewardAmendNothing : null;

    public string? MentionText => Preview is { Owners.Count: > 0 } preview
        ? _owner.Localization.FormatStewardMention(string.Join(", ", preview.Owners.Select(login => "@" + login)))
        : null;

    public string? OpenedText => Opened is { } pull ? _owner.Localization.FormatStewardOpened(pull.Number.ToString(CultureInfo.InvariantCulture)) : null;

    internal ReleaseAmendmentRequest Request => new(ListingId, Selection, Change, Reason, OnBehalfOfAuthor ? AuthorRequest.Trim() : null);

    private bool HasSelection => Scope switch
    {
        ReleaseAmendmentScope.Checked => Versions.Any(version => version.IsSelected),
        ReleaseAmendmentScope.UpTo => UpTo is not null,
        _ => true,
    };

    private bool HasChange =>
        Yank || Dependencies.Count > 0 || new[] { GameMin, GameMax, LoaderMin, LoaderMax }.Any(value => !string.IsNullOrWhiteSpace(value))
        || RemoveGameMax || Unyank || ChangeOs || RemoveLoaderMin || RemoveLoaderMax;

    private ReleaseSelection Selection => Scope switch
    {
        ReleaseAmendmentScope.UpTo => ReleaseSelection.UpTo(UpTo ?? string.Empty),
        ReleaseAmendmentScope.All => ReleaseSelection.All,
        _ => ReleaseSelection.Of(Versions.Where(version => version.IsSelected).Select(version => version.Version)),
    };

    /// <summary>The change as typed. A bound that is removed ignores the value typed for it, because its field is disabled.</summary>
    private ReleaseChange Change => new()
    {
        GameMin = Typed(GameMin),
        GameMax = RemoveGameMax ? null : Typed(GameMax),
        Yank = Yank,
        LoaderMin = RemoveLoaderMin ? null : Typed(LoaderMin),
        LoaderMax = RemoveLoaderMax ? null : Typed(LoaderMax),
        AddedDependencies = [.. Dependencies.Where(row => row.IsMissing).Select(row => new ReleaseDependencyAddition(row.Id, row.Kind))],
        DependencyBounds = [.. Dependencies.Where(row => row.TypedMin is not null || row.TypedMax is not null).Select(row => new ReleaseDependencyBounds(row.Id, row.TypedMin, row.TypedMax))],
        DependencyKinds = OnBehalfOfAuthor
            ? [.. Dependencies.Where(row => !row.IsMissing && row.NewKind is not null).Select(row => new ReleaseDependencyKind(row.Id, row.NewKind!))]
            : [],
        RemoveGameMax = RemoveGameMax,
        Unyank = Unyank,
        Os = ChangeOs ? [.. Platforms.Where(platform => platform.IsChecked).Select(platform => platform.Name)] : null,
        RemoveLoaderMin = RemoveLoaderMin,
        RemoveLoaderMax = RemoveLoaderMax,
        RemovedDependencyBounds = [.. Dependencies.Where(row => !row.IsMissing && (row.RemoveMin || row.RemoveMax)).Select(row => new ReleaseDependencyBoundRemoval(row.Id, row.RemoveMin, row.RemoveMax))],
    };

    internal Task WhenDoneAsync() => _run;

    internal void Start() => _run = LoadAsync();

    /// <summary>Drops the preview after any edit, because it no longer shows what would be sent.</summary>
    internal void Edited()
    {
        Preview = null;
        Files.Clear();
        Refusal = null;
        RefusalDetails = null;
        Notice = null;
        RefreshCommands();
    }

    partial void OnScopeChanged(ReleaseAmendmentScope value) => Edited();

    partial void OnUpToChanged(string? value) => Edited();

    partial void OnGameMinChanged(string value) => Edited();

    partial void OnGameMaxChanged(string value) => Edited();

    partial void OnLoaderMinChanged(string value) => Edited();

    partial void OnLoaderMaxChanged(string value) => Edited();

    partial void OnYankChanged(bool value)
    {
        if (value)
            Unyank = false;
        Edited();
    }

    partial void OnUnyankChanged(bool value)
    {
        if (value)
            Yank = false;
        Edited();
    }

    partial void OnRemoveGameMaxChanged(bool value) => Edited();

    partial void OnChangeOsChanged(bool value) => Edited();

    partial void OnRemoveLoaderMinChanged(bool value) => Edited();

    partial void OnRemoveLoaderMaxChanged(bool value) => Edited();

    partial void OnOnBehalfOfAuthorChanged(bool value)
    {
        if (!value)
        {
            RemoveGameMax = false;
            Unyank = false;
            ChangeOs = false;
            RemoveLoaderMin = false;
            RemoveLoaderMax = false;
        }

        foreach (var row in Dependencies)
            row.OnBehalfChanged(value);
        Edited();
    }

    partial void OnAuthorRequestChanged(string value) => Edited();

    partial void OnReasonChanged(string value) => Edited();

    partial void OnIsLoadingChanged(bool value) => RefreshCommands();

    partial void OnIsPreviewingChanged(bool value) => RefreshCommands();

    partial void OnIsOpeningChanged(bool value) => RefreshCommands();

    partial void OnOpenedChanged(ReleaseAmendmentPullRequest? value) => RefreshCommands();

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanOpen));
        PreviewCommand.NotifyCanExecuteChanged();
        OpenPullRequestCommand.NotifyCanExecuteChanged();
    }

    private void SetScope(bool selected, ReleaseAmendmentScope scope)
    {
        if (selected)
            Scope = scope;
    }

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            foreach (var version in await services.ReleaseAmendments.ReleasesAsync(ListingId))
                Versions.Add(new ReleaseAmendmentVersion(this, version));
            OnPropertyChanged(nameof(VersionNames));
            OnPropertyChanged(nameof(HasVersions));
        }
        catch (ReleaseAmendmentRefusedException exception)
        {
            // A refusal stays the same when the same files are read again, so it sets no error and offers no retry.
            Refuse(exception);
            if (exception.Refusal == ReleaseAmendmentRefusal.UnknownRelease)
                Refusal = _owner.Localization.StewardAmendNoReleases;
        }
        catch (StewardException exception)
        {
            Error = _owner.ReleaseAmendmentErrorText(exception);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => CanRetry ? _run = LoadAsync() : _run;

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private Task PreviewAsync()
    {
        // a second click while the first one runs sends nothing
        if (!CanPreview)
            return _run;

        return _run = RunPreviewAsync();
    }

    private async Task RunPreviewAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsPreviewing = true;
        Error = null;
        try
        {
            Show(await services.ReleaseAmendments.PreviewAsync(Request));
        }
        catch (ReleaseAmendmentRefusedException exception)
        {
            Refuse(exception);
        }
        catch (StewardException exception)
        {
            Error = _owner.ReleaseAmendmentErrorText(exception);
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenPullRequestAsync()
    {
        if (!CanOpen)
            return _run;

        return _run = OpenAsync(Preview!);
    }

    private async Task OpenAsync(ReleaseAmendmentPreview preview)
    {
        if (_owner.Services is not { } services)
            return;

        IsOpening = true;
        Error = null;
        try
        {
            Opened = await services.ReleaseAmendments.OpenAsync(preview);
            _owner.OnReleaseAmendmentOpened();
        }
        catch (ReleaseAmendmentChangedException exception)
        {
            Show(exception.Current);
            Notice = _owner.Localization.StewardAmendChanged;
        }
        catch (ReleaseAmendmentRefusedException exception)
        {
            Edited();
            Refuse(exception);
        }
        catch (StewardException exception)
        {
            Error = _owner.ReleaseAmendmentErrorText(exception);
        }
        finally
        {
            IsOpening = false;
        }
    }

    private void Show(ReleaseAmendmentPreview preview)
    {
        Files.Clear();
        foreach (var file in preview.Files)
            Files.Add(new ReleaseAmendmentFile(_owner, file));
        Preview = preview;
        RefreshCommands();
    }

    private void Refuse(ReleaseAmendmentRefusedException exception)
    {
        Refusal = _owner.ReleaseAmendmentRefusalText(exception.Refusal);
        RefusalDetails = string.Join(" ", exception.Details);
    }

    [RelayCommand]
    private void AddMissingDependency()
    {
        Dependencies.Add(new ReleaseAmendmentDependencyRow(this, isMissing: true));
        Edited();
    }

    [RelayCommand]
    private void BoundDependency()
    {
        Dependencies.Add(new ReleaseAmendmentDependencyRow(this, isMissing: false));
        Edited();
    }

    internal void Remove(ReleaseAmendmentDependencyRow row)
    {
        if (Dependencies.Remove(row))
            Edited();
    }

    [RelayCommand]
    private void ShowPullRequest()
    {
        if (Opened is { } pull)
            Error = _owner.TryOpenWithSystem(pull.Url.AbsoluteUri);
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        if (CanClose)
            _owner.CloseReleaseAmendment(this);
    }

    private static string? Typed(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One stamped release, which the steward checks to amend it.</summary>
public sealed partial class ReleaseAmendmentVersion(ReleaseAmendmentDialog dialog, string version) : ObservableObject
{
    public string Version { get; } = version;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => dialog.Edited();
}

/// <summary>
/// A dependency that was missing, with its kind, or a dependency that the releases state, with new bounds, and on the author's behalf a new kind or a removed bound.
/// No row removes a stated dependency, because one that the archive's mod.toml declares stays in the release.
/// </summary>
public sealed partial class ReleaseAmendmentDependencyRow : ObservableObject
{
    private readonly ReleaseAmendmentDialog _dialog;

    public ReleaseAmendmentDependencyRow(ReleaseAmendmentDialog dialog, bool isMissing)
    {
        _dialog = dialog;
        IsMissing = isMissing;
    }

    public static IReadOnlyList<string> Kinds => ListingEditor.DependencyKinds;

    /// <summary>Whether the row adds an entry that the releases do not state, which needs a kind.</summary>
    public bool IsMissing { get; }

    /// <summary>Whether the row offers a new kind and the removal of a bound for a stated entry, which only the author makes.</summary>
    public bool CanChangeKind => !IsMissing && _dialog.OnBehalfOfAuthor;

    /// <summary>The new kind of a stated entry, or null to keep its kind.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewKind))]
    private string? _newKind;

    [ObservableProperty]
    private bool _removeMin;

    [ObservableProperty]
    private bool _removeMax;

    public bool HasNewKind => NewKind is not null;

    /// <summary>The min as typed, or null when it is empty or removed.</summary>
    internal string? TypedMin => RemoveMin || string.IsNullOrWhiteSpace(Min) ? null : Min.Trim();

    /// <summary>The max as typed, or null when it is empty or removed.</summary>
    internal string? TypedMax => RemoveMax || string.IsNullOrWhiteSpace(Max) ? null : Max.Trim();

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _kind = ListingEditor.DependencyKinds[^1];

    [ObservableProperty]
    private string _min = string.Empty;

    [ObservableProperty]
    private string _max = string.Empty;

    partial void OnIdChanged(string value) => _dialog.Edited();

    partial void OnKindChanged(string value) => _dialog.Edited();

    partial void OnMinChanged(string value) => _dialog.Edited();

    partial void OnMaxChanged(string value) => _dialog.Edited();

    partial void OnNewKindChanged(string? value) => _dialog.Edited();

    partial void OnRemoveMinChanged(bool value) => _dialog.Edited();

    partial void OnRemoveMaxChanged(bool value) => _dialog.Edited();

    /// <summary>Clears what only the author changes once the amendment is no longer on the author's behalf.</summary>
    internal void OnBehalfChanged(bool onBehalf)
    {
        if (!onBehalf)
        {
            NewKind = null;
            RemoveMin = false;
            RemoveMax = false;
        }

        OnPropertyChanged(nameof(CanChangeKind));
    }

    /// <summary>Goes back to the kind that the releases state.</summary>
    [RelayCommand]
    private void KeepKind() => NewKind = null;

    [RelayCommand]
    private void Remove() => _dialog.Remove(this);
}

/// <summary>One platform of a change of os, which the steward checks when the releases run on it.</summary>
public sealed partial class ReleaseAmendmentPlatform(ReleaseAmendmentDialog dialog, string name) : ObservableObject
{
    /// <summary>The platform as a release file writes it, such as "linux".</summary>
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => dialog.Edited();
}

/// <summary>One selected release file of the preview, with its diff, or the note that it already says this.</summary>
public sealed class ReleaseAmendmentFile(MainViewModel owner, ReleaseFilePreview file)
{
    public ReleaseFilePreview File { get; } = file;

    public bool IsChanged => File.After is not null;

    public string? UnchangedText => IsChanged ? null : owner.Localization.StewardAmendUnchanged;

    public IReadOnlyList<StewardPatchLine> Lines { get; } = file.Patch?.Split('\n').Select(line => new StewardPatchLine(line)).ToList() ?? [];
}
