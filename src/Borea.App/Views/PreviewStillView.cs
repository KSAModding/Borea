using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Borea.App.ViewModels;

namespace Borea.App.Views;

/// <summary>
/// Shows the stored still of a vehicle preview in the middle part that fits its slot, and shows the child, the icon of
/// the listing, while no still is set or when the still cannot be read. The default camera of the preview frames the
/// whole vehicle inside the height of the still, so a square slot cut from its middle still shows all of it. The file
/// is read and decoded on the thread pool, as <see cref="ListingImageView"/> decodes its images. A vehicle row without an
/// icon also tells its queue when it comes near the window, by the same rule as <see cref="ListingImageView"/>, so a row
/// far away draws nothing and every other row pays for no viewport events.
/// </summary>
public sealed class PreviewStillView : Decorator
{
    /// <summary>A still is about 100 KB, so a larger file is not one that Borea stored.</summary>
    private const long MaxFileBytes = 4L * 1024 * 1024;

    public static readonly StyledProperty<string?> StillPathProperty =
        AvaloniaProperty.Register<PreviewStillView, string?>(nameof(StillPath));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<PreviewStillView>();

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<PreviewStillView>();

    public static readonly StyledProperty<string?> NoteProperty =
        AvaloniaProperty.Register<PreviewStillView, string?>(nameof(Note));

    private Bitmap? _bitmap;
    private int _generation;
    private bool _failed;
    private TextBlock? _note;
    private DiscoverItem? _row;
    private bool _nearWindow;
    private bool _inView;

    static PreviewStillView()
    {
        AffectsRender<PreviewStillView>(BackgroundProperty);
    }

    /// <summary>The PNG file of the still, or null to show the child.</summary>
    public string? StillPath
    {
        get => GetValue(StillPathProperty);
        set => SetValue(StillPathProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>A small text over the still, or null for none. Most rows never have one, so only a row with one builds its text.</summary>
    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    /// <summary>Rounds the corners of the still and of the icon, so both stay inside a rounded thumbnail border.</summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    internal bool HasStill => _bitmap is not null;

    /// <summary>Completes when the read of the current still has ended, so a test can wait for it.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The largest part of <paramref name="bitmap"/> with the aspect ratio of <paramref name="slot"/>, centered.</summary>
    internal static Rect MiddlePart(Size bitmap, Size slot)
    {
        if (slot.Width <= 0 || slot.Height <= 0)
            return new Rect(bitmap);

        var scale = Math.Min(bitmap.Width / slot.Width, bitmap.Height / slot.Height);
        var width = slot.Width * scale;
        var height = slot.Height * scale;
        return new Rect((bitmap.Width - width) / 2, (bitmap.Height - height) / 2, width, height);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StillPathProperty)
            Load();
        else if (change.Property == ChildProperty)
            UpdateChild();
        else if (change.Property == CornerRadiusProperty)
            UpdateClip();
        else if (change.Property == NoteProperty)
            UpdateNote();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var row = DataContext is DiscoverItem { HasDrawnStill: true } item ? item : null;
        if (ReferenceEquals(row, _row))
            return;

        _row?.HideFromWindow();
        if (_row is null)
            EffectiveViewportChanged += OnEffectiveViewportChanged;
        else if (row is null)
            EffectiveViewportChanged -= OnEffectiveViewportChanged;

        _row = row;

        // a row that gets another item in the same place sees no new viewport, so it asks with the one it knows
        if (row is null)
            _nearWindow = _inView = false;
        else if (_nearWindow)
            row.ShowNearWindow(_inView);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateClip();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Load();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _row?.HideFromWindow();
        _nearWindow = _inView = false;
        Release();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _note?.Measure(availableSize);
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_note is { } note)
            note.Arrange(new Rect(0, finalSize.Height - note.DesiredSize.Height, finalSize.Width, note.DesiredSize.Height));

        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is null)
            return;

        if (Background is { } background)
            context.FillRectangle(background, new Rect(Bounds.Size));

        context.DrawImage(_bitmap, MiddlePart(_bitmap.Size, Bounds.Size), new Rect(Bounds.Size));
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var viewport = e.EffectiveViewport;
        var bounds = new Rect(Bounds.Size);
        var hasPlace = IsEffectivelyVisible && Bounds.Width > 0 && Bounds.Height > 0;
        var near = hasPlace && viewport.Inflate(new Thickness(0, viewport.Height)).Intersects(bounds);
        _inView = hasPlace && viewport.Intersects(bounds);
        if (near)
            _row?.ShowNearWindow(_inView);
        else if (_nearWindow)
            _row?.HideFromWindow();

        _nearWindow = near;
    }

    private void Load()
    {
        Release();
        _failed = false;
        if (StillPath is { } path && VisualRoot is not null)
            Loading = DecodeAsync(path, _generation);

        UpdateChild();
    }

    private async Task DecodeAsync(string path, int generation)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() =>
            {
                // The bytes are read first, so the file is open only for a moment and a new still can replace it.
                byte[] bytes;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (file.Length > MaxFileBytes)
                        return null;

                    bytes = new byte[file.Length];
                    file.ReadExactly(bytes);
                }

                using var stream = new MemoryStream(bytes, writable: false);
                return new Bitmap(stream);
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A still that went away or does not decode leaves the icon.
        }

        if (generation != _generation)
        {
            bitmap?.Dispose();
            return;
        }

        _bitmap = bitmap;
        _failed = bitmap is null;
        UpdateChild();
        UpdateNote();
        InvalidateVisual();
    }

    private void Release()
    {
        _generation++;
        _bitmap?.Dispose();
        _bitmap = null;
        UpdateNote();
        InvalidateVisual();
    }

    private void UpdateNote()
    {
        if (Note is not null && _note is null)
        {
            _note = new TextBlock { Classes = { "body-sm", "still-note" }, IsHitTestVisible = false };
            LogicalChildren.Add(_note);
            VisualChildren.Add(_note);
        }

        if (_note is { } note)
        {
            note.Text = Note;
            note.IsVisible = Note is not null && _bitmap is not null;
        }
    }

    private void UpdateClip()
    {
        var radius = CornerRadius.TopLeft;
        Clip = radius > 0 ? new RectangleGeometry(new Rect(Bounds.Size), radius, radius) : null;
    }

    /// <summary>The icon stays hidden while a still loads or shows, so a row with a still never loads its icon.</summary>
    private void UpdateChild()
    {
        if (Child is { } child)
            child.IsVisible = StillPath is null || _failed;
    }
}
