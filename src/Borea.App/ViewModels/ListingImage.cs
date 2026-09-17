using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Index;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>One image record of a listing, which loads only when a view that is about to show it asks.</summary>
public sealed partial class ListingImage : ObservableObject
{
    internal static readonly TimeSpan UnavailableRetryDelay = TimeSpan.FromMinutes(5);

    private readonly MainViewModel _owner;
    private Task? _load;
    private long _failedAt;

    public ContentImage Record { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    private byte[]? _bytes;

    public bool IsLoaded => Bytes is not null;

    internal bool LoadsFromAuthorHosts => _owner.LoadImagesFromAuthorHosts;

    [ObservableProperty]
    private ContentImageFailure? _failure;

    public string? Attribution => Record.Attribution;

    public string? Source => Record.Source;

    public bool HasCredit => Attribution is not null || Source is not null;

    public ListingImage(MainViewModel owner, ContentImage record)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Record = record ?? throw new ArgumentNullException(nameof(record));
    }

    /// <summary>
    /// Loads the image once. The next call tries again after a load that could not ask the image source,
    /// after a failure that <see cref="RetryIfTurnedOff"/> forgot, and after <see cref="UnavailableRetryDelay"/> for a host that was unavailable.
    /// </summary>
    internal Task LoadAsync()
    {
        if (_load is { IsCompleted: true } && !IsLoaded && CanTryAgain())
            _load = null;

        return _load ??= LoadCoreAsync();
    }

    private bool CanTryAgain() => Failure switch
    {
        null => true,
        ContentImageFailure.Unavailable => _owner.ImageClock.GetElapsedTime(_failedAt) >= UnavailableRetryDelay,
        _ => false,
    };

    private async Task LoadCoreAsync()
    {
        if (await _owner.GetImageAsync(Record) is not { } result)
            return;

        if (!result.IsLoaded)
            _failedAt = _owner.ImageClock.GetTimestamp();

        Failure = result.Failure;
        Bytes = result.IsLoaded ? result.Bytes.ToArray() : null;
    }

    /// <summary>Forgets a failure that the image preference caused, so the image loads when a view shows it again.</summary>
    internal void RetryIfTurnedOff()
    {
        if (Failure == ContentImageFailure.DisabledByPreference)
            Failure = null;
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (Source is not null)
            _owner.OpenImageSource(Source);
    }
}

/// <summary>The images one description can show, by their id.</summary>
public sealed class DescriptionImages
{
    public const string Scheme = "ksa-image:";

    private readonly Dictionary<string, ListingImage> _byId = new(StringComparer.Ordinal);

    /// <summary>No images, so every ksa-image reference shows a missing image.</summary>
    public static DescriptionImages None { get; } = new();

    public IReadOnlyList<ListingImage> Images { get; }

    /// <param name="images">The images of the document the description resolves against: the live listing of a mod, or the one pack version a view shows.</param>
    public DescriptionImages(MainViewModel owner, ContentImages? images)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Images = (images?.Description ?? []).Select(record => new ListingImage(owner, record)).ToArray();
        foreach (var image in Images)
            _byId.Add(((DescriptionImage)image.Record).Id, image);
    }

    private DescriptionImages()
    {
        Images = [];
    }

    public static bool TryGetId(string? destination, [NotNullWhen(true)] out string? id)
    {
        id = destination is not null && destination.StartsWith(Scheme, StringComparison.Ordinal) ? destination[Scheme.Length..] : null;
        return id is not null;
    }

    public ListingImage? Find(string id) => _byId.GetValueOrDefault(id);
}
