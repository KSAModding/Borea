using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Borea.App.ViewModels;
using Borea.Core.Index;

namespace Borea.App.Views;

/// <summary>Fits the shown part of a listing image whole into its slot, shows a pulsing skeleton while the image loads, and shows the child as the placeholder when no image can show.</summary>
public sealed class ListingImageView : Decorator
{
    private static readonly TimeSpan PulseDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PulseCycle = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan PulseLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CachedFadeDuration = TimeSpan.FromMilliseconds(100);
    private static readonly BitmapShelf Shelf = new(32 * 1024 * 1024);

    public static readonly StyledProperty<ListingImage?> ImageProperty =
        AvaloniaProperty.Register<ListingImageView, ListingImage?>(nameof(Image));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<ListingImageView>();

    public static readonly StyledProperty<IBrush?> PulseBrushProperty =
        AvaloniaProperty.Register<ListingImageView, IBrush?>(nameof(PulseBrush));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<ListingImageView>();

    public static readonly StyledProperty<bool> LayoutFromRecordProperty =
        AvaloniaProperty.Register<ListingImageView, bool>(nameof(LayoutFromRecord));

    private readonly Panel _skeleton = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Panel _pulse = new() { Opacity = 0 };
    private ListingImage? _observed;
    private Bitmap? _bitmap;
    private ListingImage? _bitmapImage;
    private byte[]? _bitmapBytes;
    private byte[]? _decoding;
    private int _decodingWidth;
    private int _generation;
    private bool _nearViewport;
    private bool _decodeFailed;
    private long _pulseStartedAt;

    static ListingImageView()
    {
        AffectsRender<ListingImageView>(BackgroundProperty);
        AffectsMeasure<ListingImageView>(ImageProperty, LayoutFromRecordProperty);
    }

    public ListingImageView()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        _skeleton.Children.Add(_pulse);
        VisualChildren.Add(_skeleton);
    }

    /// <summary>What the slot shows. The placeholder shows when there is no image, when it failed, and when the preference holds it back.</summary>
    internal enum DisplayState
    {
        Placeholder,
        Loading,
        Loaded,
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

    /// <summary>The color that the loading skeleton pulses to from <see cref="Background"/>.</summary>
    public IBrush? PulseBrush
    {
        get => GetValue(PulseBrushProperty);
        set => SetValue(PulseBrushProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Sizes the slot from the shown part of the record, at most the available width, so the layout does not move when the image loads.</summary>
    public bool LayoutFromRecord
    {
        get => GetValue(LayoutFromRecordProperty);
        set => SetValue(LayoutFromRecordProperty, value);
    }

    internal DisplayState State { get; private set; }

    internal bool IsPulsing => _pulseStartedAt != 0;

    internal int? BitmapWidth => _bitmap?.PixelSize.Width;

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
            var wasPulsing = IsPulsing;
            Observe(VisualRoot is null ? null : Image);
            ReleaseBitmap();
            Show();
            RequestLoad();
            UpdateState();
            if (wasPulsing && IsPulsing)
            {
                // the new image gets its own pulse, so its fade and the pulse limit count from now
                _pulseStartedAt = 0;
                UpdatePulse(TimeSpan.Zero);
            }
        }
        else if (change.Property == ChildProperty)
        {
            UpdatePlaceholder();
        }
        else if (change.Property == BackgroundProperty)
        {
            _skeleton.Background = Background;
        }
        else if (change.Property == PulseBrushProperty)
        {
            _pulse.Background = PulseBrush;
        }
        else if (change.Property == CornerRadiusProperty)
        {
            UpdateClip();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Observe(Image);
        Show();
        UpdateState();
    }

    // stop listening while detached, so a row that is gone does not stay alive through its image
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Observe(null);
        _nearViewport = false;
        ReleaseBitmap();
        UpdateState();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateClip();
        Show();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _skeleton.Measure(availableSize);
        var measured = base.MeasureOverride(availableSize);
        if (!LayoutFromRecord || Image?.Record is not { } record)
            return measured;

        var part = ShownPart(record, new Size(record.Width, record.Height));
        var width = Math.Min(part.Width, availableSize.Width);
        return new Size(width, width * part.Height / part.Width);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _skeleton.Arrange(new Rect(finalSize));
        return base.ArrangeOverride(finalSize);
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
        UpdateState();
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
        UpdateState();
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

        if (_bitmap is null && Shelf.Take(bytes) is { } shelved)
        {
            _generation++;
            _decoding = bytes;
            _decodingWidth = shelved.PixelSize.Width;
            _bitmap = shelved;
            _bitmapImage = image;
            _bitmapBytes = bytes;
            UpdateState(fadeIn: false);
            InvalidateVisual();
        }

        // before the first layout the slot size is unknown, so the decode waits for it
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var width = DecodeWidth(image.Record, Bounds.Size, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        if (ReferenceEquals(bytes, _decoding) && (_decodeFailed || width <= _decodingWidth))
            return;

        _decoding = bytes;
        _decodingWidth = width;
        _ = DecodeAsync(image, bytes, width, ++_generation);
    }

    private async Task DecodeAsync(ListingImage image, byte[] bytes, int width, int generation)
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
        _bitmapImage = bitmap is null ? null : image;
        _bitmapBytes = bitmap is null ? null : bytes;
        _decodeFailed = bitmap is null;
        UpdateState();
        InvalidateVisual();
    }

    private void ReleaseBitmap()
    {
        if (_bitmap is null && _decoding is null)
            return;

        _generation++;
        _decoding = null;
        _decodingWidth = 0;
        _decodeFailed = false;
        // only an icon can come back, because its image is shared and keeps its bytes
        if (_bitmapImage is { Record: IconImage, Bytes: { } bytes } && ReferenceEquals(bytes, _bitmapBytes))
            Shelf.Put(bytes, _bitmap!);
        else
            _bitmap?.Dispose();
        _bitmap = null;
        _bitmapImage = null;
        _bitmapBytes = null;
        UpdateState();
        InvalidateVisual();
    }

    private void UpdateState(bool fadeIn = true)
    {
        var image = Image;
        var state = _bitmap is not null ? DisplayState.Loaded
            : _decodeFailed || image is not { Failure: null, LoadsFromAuthorHosts: true } ? DisplayState.Placeholder
            : DisplayState.Loading;

        var fade = TimeSpan.Zero;
        if (state != State)
        {
            if (fadeIn && state == DisplayState.Loaded && image is { LoadsFromAuthorHosts: true })
                fade = IsPulsing && Stopwatch.GetElapsedTime(_pulseStartedAt) >= PulseDelay ? FadeDuration : CachedFadeDuration;

            State = state;
            UpdatePlaceholder();
            _skeleton.Transitions = fade > TimeSpan.Zero ? [new DoubleTransition { Property = OpacityProperty, Duration = fade, Easing = new SineEaseInOut() }] : null;
            _skeleton.Opacity = state == DisplayState.Loading ? 1 : 0;
        }

        UpdatePulse(fade);
    }

    private void UpdatePulse(TimeSpan settle)
    {
        var pulse = State == DisplayState.Loading && _nearViewport;
        if (pulse == IsPulsing)
            return;

        _pulseStartedAt = 0;
        if (ElementComposition.GetElementVisual(_pulse) is not { } visual)
            return;

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        if (pulse)
        {
            animation.InsertKeyFrame(0, 0);
            animation.InsertKeyFrame(0.5f, 1, new SineEaseInOut());
            animation.InsertKeyFrame(1, 0, new SineEaseInOut());
            animation.Duration = PulseCycle;
            animation.DelayTime = PulseDelay;
            animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            // a load that never reports back and a hidden page do not end the pulse, so it ends by itself
            animation.IterationBehavior = AnimationIterationBehavior.Count;
            animation.IterationCount = (int)Math.Ceiling(PulseLimit / PulseCycle);
            _pulseStartedAt = Stopwatch.GetTimestamp();
        }
        else
        {
            // StopAnimation changes render thread state from the UI thread, so an animation that ends replaces the pulse
            animation.InsertKeyFrame(1, 0);
            animation.Duration = settle > TimeSpan.Zero ? settle : TimeSpan.FromMilliseconds(1);
        }

        // the element writes its own opacity on its first sync, and a changed value there drops the pending animation
        visual.Opacity = (float)_pulse.Opacity;
        visual.StartAnimation(nameof(Opacity), animation);
    }

    private void UpdatePlaceholder()
    {
        if (Child is { } placeholder)
            placeholder.IsVisible = State == DisplayState.Placeholder;
    }

    private void UpdateClip()
    {
        var radius = CornerRadius.TopLeft;
        Clip = radius > 0 ? new RectangleGeometry(new Rect(Bounds.Size), radius, radius) : null;
    }
}
