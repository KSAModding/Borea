using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Borea.App.ViewModels;
using Borea.Core.Index;

namespace Borea.App.Views;

/// <summary>Fits the shown part of a listing image whole into its slot, and shows the child as the placeholder while no image shows.</summary>
public sealed class ListingImageView : Decorator
{
    public static readonly StyledProperty<ListingImage?> ImageProperty =
        AvaloniaProperty.Register<ListingImageView, ListingImage?>(nameof(Image));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<ListingImageView>();

    public static readonly StyledProperty<bool> LayoutFromRecordProperty =
        AvaloniaProperty.Register<ListingImageView, bool>(nameof(LayoutFromRecord));

    private ListingImage? _observed;
    private Bitmap? _bitmap;
    private byte[]? _decoding;
    private int _decodingWidth;
    private int _generation;
    private bool _nearViewport;

    static ListingImageView()
    {
        AffectsRender<ListingImageView>(BackgroundProperty);
        AffectsMeasure<ListingImageView>(ImageProperty, LayoutFromRecordProperty);
    }

    public ListingImageView()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public ListingImage? Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Sizes the slot from the shown part of the record, at most the available width, so the layout does not move when the image loads.</summary>
    public bool LayoutFromRecord
    {
        get => GetValue(LayoutFromRecordProperty);
        set => SetValue(LayoutFromRecordProperty, value);
    }

    /// <summary>The largest rectangle with the image's aspect ratio inside the slot, centered, so the image is neither stretched nor cropped.</summary>
    internal static Rect Fit(Size image, Size slot)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return default;

        var scale = Math.Min(slot.Width / image.Width, slot.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        return new Rect((slot.Width - width) / 2, (slot.Height - height) / 2, width, height);
    }

    /// <summary>The part of a bitmap of <paramref name="record"/> that a view shows, which is the center square for an icon and the whole bitmap for any other image.</summary>
    internal static Rect ShownPart(ContentImage record, Size bitmap)
    {
        if (record is not IconImage icon)
            return new Rect(bitmap);

        var square = icon.CenterSquare;
        var scaleX = bitmap.Width / icon.Width;
        var scaleY = bitmap.Height / icon.Height;
        return new Rect(square.X * scaleX, square.Y * scaleY, square.Side * scaleX, square.Side * scaleY);
    }

    internal static (Rect Source, Rect Destination) Placement(ContentImage record, Size bitmap, Size slot)
    {
        var part = ShownPart(record, bitmap);
        return (part, Fit(part.Size, slot));
    }

    /// <summary>The width to decode the whole image at, so the shown part gets no more pixels than the slot shows and never more than the image has.</summary>
    internal static int DecodeWidth(ContentImage record, Size slot, double scaling)
    {
        var part = ShownPart(record, new Size(record.Width, record.Height));
        var shown = slot.Width > 0 && slot.Height > 0 ? Fit(part.Size, slot).Width : part.Width;
        return Math.Clamp((int)Math.Ceiling(Math.Ceiling(shown * scaling) * record.Width / part.Width), 1, record.Width);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImageProperty)
        {
            Observe(VisualRoot is null ? null : Image);
            ReleaseBitmap();
            Show();
            RequestLoad();
        }
        else if (change.Property == ChildProperty)
        {
            UpdatePlaceholder();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Observe(Image);
        Show();
    }

    // stop listening while detached, so a row that is gone does not stay alive through its image
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Observe(null);
        _nearViewport = false;
        ReleaseBitmap();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Show();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        if (!LayoutFromRecord || Image?.Record is not { } record)
            return measured;

        var part = ShownPart(record, new Size(record.Width, record.Height));
        var width = Math.Min(part.Width, availableSize.Width);
        return new Size(width, width * part.Height / part.Width);
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is null || Image?.Record is not { } record)
            return;

        if (Background is { } background)
            context.FillRectangle(background, new Rect(Bounds.Size));

        var (source, destination) = Placement(record, _bitmap.Size, Bounds.Size);
        context.DrawImage(_bitmap, source, destination);
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var viewport = e.EffectiveViewport;
        _nearViewport = IsEffectivelyVisible
            && Bounds.Width > 0
            && Bounds.Height > 0
            && viewport.Inflate(new Thickness(0, viewport.Height)).Intersects(new Rect(Bounds.Size));
        RequestLoad();
    }

    private void Observe(ListingImage? image)
    {
        if (_observed is not null)
            _observed.PropertyChanged -= OnImageChanged;

        _observed = image;
        if (image is not null)
            image.PropertyChanged += OnImageChanged;
    }

    private void OnImageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ListingImage.Bytes) or nameof(ListingImage.Failure)))
            return;

        Show();
        RequestLoad();
    }

    private void RequestLoad()
    {
        if (_nearViewport && Image is { IsLoaded: false } image)
            _ = image.LoadAsync();
    }

    private void Show()
    {
        if (Image is not { Bytes: { } bytes } image || VisualRoot is null)
        {
            ReleaseBitmap();
            return;
        }

        var width = DecodeWidth(image.Record, Bounds.Size, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        if (ReferenceEquals(bytes, _decoding) && width <= _decodingWidth)
            return;

        _decoding = bytes;
        _decodingWidth = width;
        _ = DecodeAsync(bytes, width, ++_generation);
    }

    private async Task DecodeAsync(byte[] bytes, int width, int generation)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return Bitmap.DecodeToWidth(stream, width);
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // verified bytes that still do not decode keep the placeholder
        }

        if (generation != _generation)
        {
            bitmap?.Dispose();
            return;
        }

        _bitmap?.Dispose();
        _bitmap = bitmap;
        UpdatePlaceholder();
        InvalidateVisual();
    }

    private void ReleaseBitmap()
    {
        if (_bitmap is null && _decoding is null)
            return;

        _generation++;
        _decoding = null;
        _decodingWidth = 0;
        _bitmap?.Dispose();
        _bitmap = null;
        UpdatePlaceholder();
        InvalidateVisual();
    }

    private void UpdatePlaceholder()
    {
        if (Child is { } placeholder)
            placeholder.IsVisible = _bitmap is null;
    }
}
