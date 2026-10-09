using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Listings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>A curated tag of the content index, which the author switches on or off.</summary>
public sealed partial class ListingTagChip : ObservableObject
{
    private readonly ListingEditor _owner;

    public ListingTagChip(ListingEditor owner, string tag, string name, string meaning, bool isSelected)
    {
        _owner = owner;
        Tag = tag;
        Name = name;
        Meaning = meaning;
        _isSelected = isSelected;
    }

    public string Tag { get; }

    public string Name { get; }

    public string Meaning { get; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;

    partial void OnIsSelectedChanged(bool value) => _owner.TagChipChanged(Tag, value);
}

/// <summary>
/// One [[dependencies]] entry. An entry the page cannot edit, such as one with any_of, is shown and kept as it is.
/// The versions offer the stamped releases of the named mod, and the mistakes the checks find in the entry show under it.
/// </summary>
public sealed partial class ListingDependencyRow : ObservableObject
{
    private readonly ListingEditor _owner;
    private readonly AuthoredTable? _preserved;

    public ListingDependencyRow(ListingEditor owner, ListingDependency dependency)
    {
        _owner = owner;
        _preserved = dependency.Preserved;
        _id = dependency.Id;
        _kind = dependency.Kind;
        _min = dependency.Min ?? string.Empty;
        _max = dependency.Max ?? string.Empty;
        _versions = owner.DependencyVersions(dependency.Id.Trim());
        KindChoices = ListingEditor.DependencyKinds.Contains(dependency.Kind, StringComparer.Ordinal) ? ListingEditor.DependencyKinds : [dependency.Kind, .. ListingEditor.DependencyKinds];
    }

    public bool IsEditable => _preserved is null;

    public bool IsPreserved => _preserved is not null;

    /// <summary>The ids a kept entry names, with its kind.</summary>
    public string PreservedText => _owner.PreservedDependencyText(
        $"{Kind}: {string.Join(", ", _preserved?.GetList("any_of")?.OfType<AuthoredTable>().Select(alternative => alternative.GetString("id")).OfType<string>() ?? [Id])}");

    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindText), nameof(HasKindText), nameof(CanNeedNewest))]
    private string _kind;

    [ObservableProperty]
    private string _min;

    [ObservableProperty]
    private string _max;

    /// <summary>The stamped releases of the named mod, newest first, which the version fields offer. The fields still take any text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersions), nameof(HasNoVersions), nameof(CanNeedNewest))]
    private IReadOnlyList<ListingReleaseChoice> _versions;

    public bool HasVersions => IsEditable && Versions.Count > 0;

    /// <summary>Whether the version fields are plain text fields, because the named mod has no stamped release to offer.</summary>
    public bool HasNoVersions => IsEditable && Versions.Count == 0;

    /// <summary>Whether "needs the newest" fits: the named mod has releases, and the entry does not name versions that conflict.</summary>
    public bool CanNeedNewest => HasVersions && Kind != "conflict";

    public string NeedsNewestHint => _owner.NeedsNewestHint;

    /// <summary>The kinds the index knows, and first a kind it does not know when the entry came with one, so the list can show it.</summary>
    public IReadOnlyList<string> KindChoices { get; }

    /// <summary>What a client does with the entry of the chosen kind.</summary>
    public string? KindText => _owner.DependencyKindText(Kind);

    public bool HasKindText => IsEditable && KindText is not null;

    /// <summary>The ids the entry names: its id, or each id of its alternatives.</summary>
    internal IEnumerable<string> NamedIds => _preserved?.GetList("any_of") is { } alternatives
        ? alternatives.OfType<AuthoredTable>().Select(alternative => alternative.GetString("id")).OfType<string>()
        : [Id];

    /// <summary>The errors that the checks give for the entry, one per line, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    private string? _errorText;

    public bool HasErrors => ErrorText is not null;

    /// <summary>The notes for the entry, such as an id the index does not list, one per line, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string? _noteText;

    public bool HasNotes => NoteText is not null;

    internal ListingDependency ToDependency() => new(Id.Trim(), Kind, Empty(Min), Empty(Max)) { Preserved = _preserved };

    internal void RefreshText()
    {
        RefreshVersions();
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(HasKindText));
        OnPropertyChanged(nameof(NeedsNewestHint));
    }

    /// <summary>Takes the releases of the named mod again, and keeps the list when they are the same, so an open list stays as it is.</summary>
    private void RefreshVersions()
    {
        var versions = _owner.DependencyVersions(Id.Trim());
        if (!versions.SequenceEqual(Versions))
            Versions = versions;
    }

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    /// <summary>Sets the oldest version to the newest stable release of the named mod, or its newest release when none is stable.</summary>
    [RelayCommand]
    private void NeedsNewest()
    {
        if (_owner.NewestDependencyVersion(Id.Trim()) is { } newest)
            Min = newest;
    }

    partial void OnIdChanged(string value)
    {
        RefreshVersions();
        _owner.Refresh();
    }

    partial void OnKindChanged(string value) => _owner.Refresh();

    partial void OnMinChanged(string value) => _owner.Refresh();

    partial void OnMaxChanged(string value) => _owner.Refresh();

    private static string? Empty(string value) => value.Trim().Length == 0 ? null : value.Trim();
}

/// <summary>
/// One [[mods]] entry of a pack: a listed mod and the release it pins. A pin no client can install shows why, a pin whose mod
/// has a newer release offers it, and a pin that a retraction of the listed pack names shows what the retraction says.
/// </summary>
public sealed partial class ListingPackMemberRow : ObservableObject
{
    private readonly ListingEditor _owner;
    private readonly ListingPackMember _member;

    public ListingPackMemberRow(ListingEditor owner, ListingPackMember member, string name, IReadOnlyList<ListingReleaseChoice> releases)
    {
        _owner = owner;
        _member = member;
        Name = name;
        Releases = releases;
        _selected = releases.FirstOrDefault(release => release.Version == member.Version);
        RetractionNote = owner.RetractionNotes(member.Id) is { Count: > 0 } notes ? string.Join("\n", notes) : null;
    }

    public string Id => _member.Id;

    public string Name { get; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ListingReleaseChoice> Releases { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note), nameof(HasNote), nameof(Newer), nameof(NewerText), nameof(UseNewerText), nameof(HasNewer))]
    private ListingReleaseChoice? _selected;

    public string? Note => _owner.MemberNote(ToMember());

    public bool HasNote => Note is not null;

    /// <summary>The newest release that is newer than the pin and at least as stable. A hint for the author, the pin stays until they move it.</summary>
    public ListingReleaseChoice? Newer => _owner.NewerChoice(Id, ToMember().Version, Releases);

    public string? NewerText => Newer is { } newer ? _owner.NewerText(newer.Version) : null;

    public string? UseNewerText => Newer is { } newer ? _owner.UseNewerText(newer.Version) : null;

    public bool HasNewer => Newer is not null;

    public string? RetractionNote { get; }

    public bool IsNamedInRetraction => RetractionNote is not null;

    internal ListingPackMember ToMember() => _member with { Version = Selected?.Version ?? _member.Version };

    [RelayCommand]
    private void UseNewer()
    {
        if (Newer is { } newer)
            Selected = newer;
    }

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    partial void OnSelectedChanged(ListingReleaseChoice? value) => _owner.Refresh();
}

/// <param name="Status">The release status, or empty for a pinned release that the index does not offer or a release that needs no mark.</param>
public sealed record ListingReleaseChoice(string Version, string Status)
{
    public bool HasStatus => Status.Length > 0;

    /// <summary>The version, which an editable list writes into its field when the author picks the release.</summary>
    public override string ToString() => Version;
}

/// <summary>A dependency that the mod.toml of the author's release declares, which the index derives by itself. It is shown read only.</summary>
public sealed partial class ListingDeclaredDependencyRow : ObservableObject
{
    private readonly ListingEditor _owner;

    public ListingDeclaredDependencyRow(ListingEditor owner, ListingDeclaredDependency dependency)
    {
        _owner = owner;
        Dependency = dependency;
    }

    public ListingDeclaredDependency Dependency { get; }

    /// <summary>Such as "Required by your mod.toml: StarMap".</summary>
    public string Text => _owner.DeclaredDependencyText(Dependency);

    /// <summary>Whether the index lists the id as a loader, whose bounds go into [loader].</summary>
    public bool IsLoader => _owner.IsListedLoader(Dependency.Id);

    public string ModLoaderText => _owner.ModLoaderText;

    /// <summary>Whether no entry of the form and not [loader] names the id yet, so bounds can be added.</summary>
    public bool CanAddBounds => !_owner.HasDependencyEntry(Dependency.Id) && !_owner.IsLoaderSet(Dependency.Id);

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsLoader));
        OnPropertyChanged(nameof(ModLoaderText));
        OnPropertyChanged(nameof(CanAddBounds));
    }

    /// <summary>
    /// Adds an editable entry with the id and the kind, which then replaces the derived one in each release. For a loader it
    /// turns on [loader] instead.
    /// </summary>
    [RelayCommand]
    private void AddBounds() => _owner.AddDependencyEntry(Dependency.Id, Dependency.Kind);
}

/// <summary>
/// The icon or one description image. Measuring a local file or the hosted image fills in the facts of the record;
/// for a local file the author gives the HTTPS address where it is or will be hosted.
/// </summary>
public sealed partial class ListingImageRow : ObservableObject
{
    private readonly ListingEditor _owner;

    public ListingImageRow(ListingEditor owner, ListingImageRole role, ListingImageRecord record)
    {
        _owner = owner;
        Role = role;
        _id = record.Id ?? string.Empty;
        _url = record.Url;
        _sha256 = record.Sha256;
        _width = record.Width;
        _height = record.Height;
        _size = record.Size;
        _license = record.License ?? string.Empty;
        _attribution = record.Attribution ?? string.Empty;
        _source = record.Source ?? string.Empty;
    }

    public ListingImageRole Role { get; }

    public bool IsDescriptionImage => Role == ListingImageRole.Description;

    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _url;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FactsText), nameof(IsMeasured))]
    private string? _sha256;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FactsText))]
    private long? _width;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FactsText))]
    private long? _height;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FactsText))]
    private long? _size;

    [ObservableProperty]
    private string _license;

    [ObservableProperty]
    private string _attribution;

    [ObservableProperty]
    private string _source;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MeasureCommand))]
    private bool _isMeasuring;

    /// <summary>Why the last measurement did not give a record, or null.</summary>
    [ObservableProperty]
    private string? _problem;

    public bool IsMeasured => Sha256 is not null;

    /// <summary>The pixel size and the byte size, such as "512 x 512 px, 150,604 bytes".</summary>
    public string FactsText => IsMeasured ? _owner.ImageFacts(Width, Height, Size) : _owner.ImageNotMeasured;

    internal void RefreshText() => OnPropertyChanged(nameof(FactsText));

    internal void Apply(ListingImageMeasurement measurement)
    {
        var measured = measurement.IsMeasured;
        Problem = measurement.Problem;
        Width = measured ? measurement.Width : null;
        Height = measured ? measurement.Height : null;
        Size = measured ? measurement.Size : null;
        Sha256 = measurement.Sha256;
    }

    internal ListingImageRecord ToRecord() => new(Url.Trim())
    {
        Id = IsDescriptionImage ? Empty(Id) : null,
        Sha256 = Sha256,
        Width = Width,
        Height = Height,
        Size = Size,
        License = Empty(License),
        Attribution = Empty(Attribution),
        Source = Empty(Source),
    };

    [RelayCommand]
    private Task ChooseFileAsync() => _owner.PickImageFileAsync(this);

    [RelayCommand(CanExecute = nameof(CanMeasure))]
    private Task MeasureAsync() => _owner.MeasureImageAsync(this);

    private bool CanMeasure() => !IsMeasuring;

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    partial void OnIdChanged(string value) => _owner.Refresh();

    partial void OnUrlChanged(string value) => _owner.Refresh();

    partial void OnSha256Changed(string? value) => _owner.Refresh();

    partial void OnLicenseChanged(string value) => _owner.Refresh();

    partial void OnAttributionChanged(string value) => _owner.Refresh();

    partial void OnSourceChanged(string value) => _owner.Refresh();

    private static string? Empty(string value) => value.Trim().Length == 0 ? null : value.Trim();
}
