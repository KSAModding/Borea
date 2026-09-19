using System;
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

    partial void OnIsSelectedChanged(bool value) => _owner.Refresh();
}

/// <summary>One [[dependencies]] entry. An entry the page cannot edit, such as one with any_of, is shown and kept as it is.</summary>
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
    }

    public bool IsEditable => _preserved is null;

    public bool IsPreserved => _preserved is not null;

    /// <summary>The ids a kept entry names, with its kind.</summary>
    public string PreservedText => _owner.PreservedDependencyText(
        $"{Kind}: {string.Join(", ", _preserved?.GetList("any_of")?.OfType<AuthoredTable>().Select(alternative => alternative.GetString("id")).OfType<string>() ?? [Id])}");

    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _kind;

    [ObservableProperty]
    private string _min;

    [ObservableProperty]
    private string _max;

    internal ListingDependency ToDependency() => new(Id.Trim(), Kind, Empty(Min), Empty(Max)) { Preserved = _preserved };

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    partial void OnIdChanged(string value) => _owner.Refresh();

    partial void OnKindChanged(string value) => _owner.Refresh();

    partial void OnMinChanged(string value) => _owner.Refresh();

    partial void OnMaxChanged(string value) => _owner.Refresh();

    private static string? Empty(string value) => value.Trim().Length == 0 ? null : value.Trim();
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
