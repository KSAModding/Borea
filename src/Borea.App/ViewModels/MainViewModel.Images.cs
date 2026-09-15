using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Index;
using Borea.Core.ModPacks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>Listing icons and description images (RFC 0058).</summary>
public partial class MainViewModel
{
    private readonly Dictionary<IconKey, ListingImage> _icons = [];

    private bool? _loadImagesFromAuthorHosts;

    [ObservableProperty]
    private DescriptionImages _contentDescriptionImages = DescriptionImages.None;

    [ObservableProperty]
    private DescriptionImages _packDescriptionImages = DescriptionImages.None;

    internal TimeProvider ImageClock { get; set; } = TimeProvider.System;

    /// <summary>Turning it on lets the images it held back load when a view shows them again.</summary>
    public bool LoadImagesFromAuthorHosts
    {
        get => _loadImagesFromAuthorHosts ?? _appPreferences.LoadImagesFromAuthorHosts;
        set
        {
            if (value == LoadImagesFromAuthorHosts)
                return;

            _loadImagesFromAuthorHosts = value;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithLoadImagesFromAuthorHosts(value));
            if (!value)
                return;

            foreach (var image in _icons.Values.Concat(ContentDescriptionImages.Images).Concat(PackDescriptionImages.Images))
                image.RetryIfTurnedOff();
        }
    }

    /// <summary>One image per icon record, so a Home tile, a row and a page header load the same icon once.</summary>
    internal ListingImage? IconFor(IconImage? record)
    {
        if (record is null)
            return null;

        var key = new IconKey(record.Url, record.Sha256, record.Width, record.SizeBytes, record.License, record.Attribution, record.Source);
        if (!_icons.TryGetValue(key, out var icon))
        {
            icon = new ListingImage(this, record);
            _icons.Add(key, icon);
        }

        return icon;
    }

    /// <summary>The images of the pack version a view shows, which are facts of that version alone.</summary>
    internal static ContentImages? ImagesOf(ContentIndexPack? pack, ModPackMetadata version)
        => pack?.Versions.FirstOrDefault(candidate => candidate.Metadata.Version == version.Version)?.Images;

    /// <summary>Null when no image source could be asked, so the image tries again the next time it is shown.</summary>
    internal async Task<ContentImageResult?> GetImageAsync(ContentImage record)
    {
        if (_services is not { } services)
            return null;

        try
        {
            var result = await services.Images.GetAsync(record, LoadImagesFromAuthorHosts);
            if (result.Failure is not (null or ContentImageFailure.DisabledByPreference))
                services.Log.Write($"The image {record.Url} is not shown. {result.Reason}");

            return result;
        }
        catch (ObjectDisposedException)
        {
            // a settings change replaced the services while the image loaded
            return null;
        }
    }

    internal void OpenImageSource(string url)
    {
        if (TryOpenUrl(url) is not { } error)
            return;

        if (CurrentWindowPack)
            PackDetailError = error;
        else
            ContentDetailError = error;
    }

    private readonly record struct IconKey(string Url, string Sha256, int Side, long SizeBytes, string? License, string? Attribution, string? Source);
}
