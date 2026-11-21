using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Borea.Core.Screenshots;

namespace Borea.App.Views;

/// <summary>
/// Shows the middle part of a screenshot that fits the slot. A screenshot of the game can be a 4K PNG of 11 MB, so the picture decodes on the
/// thread pool at a small width, and only two decode at a time, so a fast scroll through a large folder does not start a decode for every tile
/// that it passes. A tile that leaves before its turn decodes nothing.
/// </summary>
public sealed class ScreenshotThumbnail : Control
{
    /// <summary>About the width of the largest tile at a display scale of 150 percent.</summary>
    internal const int DecodeWidth = 320;

    /// <summary>Some screens of the largest tiles, which is about 140 thumbnails of 16:9.</summary>
    internal static readonly ScreenshotThumbnailCache Cache = new(32L * 1024 * 1024);

    private static readonly SemaphoreSlim Decodes = new(2);

    public static readonly StyledProperty<Screenshot?> ScreenshotProperty =
        AvaloniaProperty.Register<ScreenshotThumbnail, Screenshot?>(nameof(Screenshot));

    private Screenshot? _shown;
    private Bitmap? _bitmap;
    private CancellationTokenSource? _load;

    static ScreenshotThumbnail()
    {
        AffectsRender<ScreenshotThumbnail>(ScreenshotProperty);
    }

    public ScreenshotThumbnail()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public Screenshot? Screenshot
    {
        get => GetValue(ScreenshotProperty);
        set => SetValue(ScreenshotProperty, value);
    }

    internal bool HasPicture => _bitmap is not null;

    /// <summary>Completes when the decode of the current screenshot has ended, so a test can wait for it.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScreenshotProperty && VisualRoot is not null)
            Show();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Release();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is { } bitmap)
            context.DrawImage(bitmap, PreviewStillView.MiddlePart(bitmap.Size, Bounds.Size), new Rect(Bounds.Size));
    }

    private void Show()
    {
        Release();
        if (Screenshot is not { } screenshot)
            return;

        if (Cache.Acquire(screenshot) is { } cached)
        {
            ShowBitmap(screenshot, cached);
            return;
        }

        _load = new CancellationTokenSource();
        Loading = LoadAsync(screenshot, _load.Token);
    }

    private async Task LoadAsync(Screenshot screenshot, CancellationToken cancellationToken)
    {
        Bitmap? bitmap = null;
        try
        {
            await Decodes.WaitAsync(cancellationToken);
            try
            {
                if (!cancellationToken.IsCancellationRequested)
                    bitmap = await Task.Run(() => Decode(screenshot.Path));
            }
            finally
            {
                Decodes.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A screenshot that went away or does not decode leaves the tile empty.
            return;
        }

        if (bitmap is null)
            return;

        // a tile that left keeps the thumbnail in the cache, because a scroll back shows it again
        var held = Cache.Add(screenshot, bitmap);
        if (cancellationToken.IsCancellationRequested)
            Cache.Release(screenshot);
        else
            ShowBitmap(screenshot, held);
    }

    private static Bitmap Decode(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Bitmap.DecodeToWidth(file, DecodeWidth);
    }

    private void ShowBitmap(Screenshot screenshot, Bitmap bitmap)
    {
        _shown = screenshot;
        _bitmap = bitmap;
        InvalidateVisual();
    }

    private void Release()
    {
        _load?.Cancel();
        _load = null;
        if (_shown is { } shown)
            Cache.Release(shown);

        _shown = null;
        _bitmap = null;
        InvalidateVisual();
    }
}
