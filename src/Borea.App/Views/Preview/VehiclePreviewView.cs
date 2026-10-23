using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;
using PreviewRenderOptions = Borea.Preview.Rendering.RenderOptions;

namespace Borea.App.Views.Preview;

/// <summary>
/// Shows a <see cref="PreviewScene"/> that the player can turn with a drag or the arrow keys and zoom
/// with the wheel. The software renderer draws each frame on a background task into a bitmap that this
/// control stretches over its bounds, and it draws only when the scene, the size or the camera changes.
/// While a drag holds the pointer, the frames render at <see cref="PreviewRenderOptions.DragScale"/> of the
/// full resolution, or at <see cref="PreviewRenderOptions.SlowDragScale"/> when <see cref="DragScaleController"/>
/// finds the drag frames too slow. Each new drag starts at the normal drag scale again.
/// </summary>
public sealed class VehiclePreviewView : Control
{
    /// <summary>How far one arrow key press turns the camera, 10 degrees.</summary>
    internal const float KeyStepPixels = 10f * MathF.PI / 180f / OrbitCamera.RadiansPerPixel;

    public static readonly StyledProperty<PreviewScene?> SceneProperty =
        AvaloniaProperty.Register<VehiclePreviewView, PreviewScene?>(nameof(Scene));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<VehiclePreviewView>();

    // The renderer clears to transparent and the Background brush shows through, so the bitmap uses straight alpha.
    private static readonly PreviewRenderOptions Options = new();

    // A drag frame shows for a short time only, so it leaves out the parts that are smaller than one of its pixels.
    private static readonly PreviewRenderOptions DragOptions = Options with { SkipSubPixelDraws = true };

    // The pitch in drag pixels stays inside the clamp of OrbitCamera.Orbit, so a drag back responds at once.
    private static readonly float MinOrbitY = (-OrbitCamera.MaxPitch - OrbitCamera.DefaultPitch) / OrbitCamera.RadiansPerPixel;
    private static readonly float MaxOrbitY = (OrbitCamera.MaxPitch - OrbitCamera.DefaultPitch) / OrbitCamera.RadiansPerPixel;

    // The zoom steps stay inside the 0.15 to 4 times distance clamp of OrbitCamera.Zoom for the same reason.
    private static readonly float MinZoomSteps = MathF.Log(4f) / MathF.Log(OrbitCamera.ZoomStepFactor);
    private static readonly float MaxZoomSteps = MathF.Log(0.15f) / MathF.Log(OrbitCamera.ZoomStepFactor);

    // The renderer keeps the vertices of the scene between frames, and only one frame renders at a time.
    private readonly SoftwareRenderer _renderer = new();
    private OrbitCamera _camera = new();
    private readonly DragScaleController _dragScales = new(Options);
    private FrameBuffer? _fullFrame;
    private FrameBuffer? _dragFrame;
    private float _orbitX;
    private float _orbitY;
    private float _zoomSteps;
    private Point? _dragFrom;
    private WriteableBitmap? _bitmap;
    private RenderRequest? _queued;
    private bool _rendering;
    private int _generation;
    private CancellationTokenSource _cancellation = new();

    static VehiclePreviewView()
    {
        AffectsRender<VehiclePreviewView>(BackgroundProperty);
        FocusableProperty.OverrideDefaultValue<VehiclePreviewView>(true);
    }

    public PreviewScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    internal float CameraDistance => _camera.Distance;

    internal int FramesShown { get; private set; }

    /// <summary>The scene of the frame that shows now, or null when only the background shows.</summary>
    internal PreviewScene? ShownScene { get; private set; }

    internal bool IsIdle => !_rendering && _queued is null;

    internal int? BitmapWidth => _bitmap?.PixelSize.Width;

    /// <summary>The scale that the next drag frame renders at.</summary>
    internal double DragScale => _dragScales.Scale;

    /// <summary>Frames the whole scene again from the default angles.</summary>
    public void ResetView()
    {
        _orbitX = 0;
        _orbitY = 0;
        _zoomSteps = 0;
        RequestRender(full: true);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background is { } background)
            context.FillRectangle(background, bounds);

        if (_bitmap is not null)
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), bounds);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            StartGeneration();
            ReleaseBitmap();
            ReleaseRenderState();
            _dragScales.Reset();
            _orbitX = 0;
            _orbitY = 0;
            _zoomSteps = 0;
            RequestRender(full: true);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestRender(full: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StartGeneration();
        _dragFrom = null;
        ReleaseBitmap();
        ReleaseRenderState();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RequestRender(full: true);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        Focus(NavigationMethod.Pointer);
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _dragFrom = null;
            ResetView();
            return;
        }

        _dragFrom = point.Position;
        _dragScales.Reset();
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from || e.Pointer.Captured != this)
            return;

        var position = e.GetPosition(this);
        _dragFrom = position;
        Orbit((float)(position.X - from.X), (float)(position.Y - from.Y), full: false);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is null)
            return;

        // Clearing the drag first lets the capture-lost handler see that the drag already ended.
        _dragFrom = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        RequestRender(full: true);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragFrom is null)
            return;

        _dragFrom = null;
        RequestRender(full: true);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoomSteps = Math.Clamp(_zoomSteps + (float)e.Delta.Y, MinZoomSteps, MaxZoomSteps);
        e.Handled = true;
        RequestRender(full: _dragFrom is null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-KeyStepPixels, 0f),
            Key.Right => (KeyStepPixels, 0f),
            Key.Up => (0f, -KeyStepPixels),
            Key.Down => (0f, KeyStepPixels),
            _ => (0f, 0f),
        };
        if (dx == 0 && dy == 0)
            return;

        e.Handled = true;
        Orbit(dx, dy, full: true);
    }

    private void Orbit(float dx, float dy, bool full)
    {
        var turn = 2f * MathF.PI / OrbitCamera.RadiansPerPixel;
        _orbitX = MathF.IEEERemainder(_orbitX + dx, turn);
        _orbitY = Math.Clamp(_orbitY + dy, MinOrbitY, MaxOrbitY);
        RequestRender(full);
    }

    /// <summary>
    /// Builds a new camera for the current scene, size and pose, and queues a frame for it. A frame that
    /// waits in the queue is replaced, so only the newest camera renders. Each camera is new and is not
    /// changed after this, so the background task can read it while the player keeps turning the view.
    /// </summary>
    private void RequestRender(bool full)
    {
        var size = Bounds.Size;
        var aspect = size.Width > 0 && size.Height > 0 ? (float)(size.Width / size.Height) : 1f;
        var scene = Scene;
        var camera = new OrbitCamera();
        camera.Frame(scene?.Bounds ?? PreviewBounds.Empty, aspect);
        camera.Orbit(_orbitX, _orbitY);
        camera.Zoom(_zoomSteps);
        _camera = camera;

        if (VisualRoot is null || scene is null || size.Width <= 0 || size.Height <= 0)
            return;

        var frameScale = full ? 1 : _dragScales.Scale;
        var scale = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * frameScale;
        var width = Math.Clamp((int)Math.Ceiling(size.Width * scale), 1, FrameBuffer.MaxSize);
        var height = Math.Clamp((int)Math.Ceiling(size.Height * scale), 1, FrameBuffer.MaxSize);
        _queued = new RenderRequest(scene, camera, width, height, full ? null : frameScale, _generation, _cancellation);
        if (!_rendering)
            _ = RenderQueuedAsync();
    }

    private async Task RenderQueuedAsync()
    {
        _rendering = true;
        try
        {
            while (_queued is { } request)
            {
                _queued = null;
                FrameBuffer? frame = null;
                var elapsedMs = double.NaN;
                try
                {
                    var token = request.Cancellation.Token;
                    var reused = TakeFrameBuffer(request);
                    var options = request.DragScale is null ? Options : DragOptions;
                    (frame, elapsedMs) = await Task.Run(() =>
                    {
                        var buffer = reused ?? new FrameBuffer(request.Width, request.Height);
                        var watch = Stopwatch.StartNew();
                        _renderer.Render(request.Scene, request.Camera, buffer, options, token);
                        return (buffer, watch.Elapsed.TotalMilliseconds);
                    }, token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // A scene that the renderer rejects keeps the frame that shows now.
                }

                // A source of an older generation has no other render that uses its token any more.
                if (request.Cancellation != _cancellation)
                    request.Cancellation.Dispose();

                if (frame is not null && request.Generation == _generation)
                {
                    Show(frame, request.Scene);
                    KeepFrameBuffer(request, frame);
                    if (request.DragScale is { } dragScale)
                        _dragScales.Record(dragScale, elapsedMs);
                }
            }
        }
        finally
        {
            _rendering = false;

            // A detach or a scene change during the render could not release what the render still used.
            if (VisualRoot is null || Scene is null)
                ReleaseRenderState();
        }
    }

    /// <summary>
    /// The buffer of the last frame of the same kind when it has the size of <paramref name="request"/>.
    /// Show copies a frame into the bitmap before the next render starts, so the buffer is free again.
    /// </summary>
    private FrameBuffer? TakeFrameBuffer(RenderRequest request)
    {
        var buffer = request.DragScale is null ? _fullFrame : _dragFrame;
        return buffer is not null && buffer.Width == request.Width && buffer.Height == request.Height ? buffer : null;
    }

    private void KeepFrameBuffer(RenderRequest request, FrameBuffer frame)
    {
        if (request.DragScale is null)
            _fullFrame = frame;
        else
            _dragFrame = frame;
    }

    /// <summary>
    /// Drops the cached vertices of the renderer and the frame buffers. A running render still uses them, so
    /// it waits for the render to end, which calls this again.
    /// </summary>
    private void ReleaseRenderState()
    {
        if (_rendering)
            return;

        _renderer.Release();
        _fullFrame = null;
        _dragFrame = null;
    }

    private void Show(FrameBuffer frame, PreviewScene scene)
    {
        var size = new PixelSize(frame.Width, frame.Height);
        if (_bitmap is null || _bitmap.PixelSize != size)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        }

        using (var buffer = _bitmap.Lock())
        {
            var rowBytes = frame.Width * 4;
            for (var row = 0; row < frame.Height; row++)
                Marshal.Copy(frame.Rgba, row * rowBytes, buffer.Address + row * buffer.RowBytes, rowBytes);
        }

        ShownScene = scene;
        FramesShown++;
        InvalidateVisual();
    }

    /// <summary>Makes every running and queued frame stale, so a frame of the old scene or of a detached view never shows.</summary>
    private void StartGeneration()
    {
        _generation++;
        _queued = null;
        _cancellation.Cancel();
        // The running frame still holds the old source and disposes it when it ends, and an idle view does it here.
        if (!_rendering)
            _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
    }

    private void ReleaseBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        ShownScene = null;
        InvalidateVisual();
    }

    /// <param name="DragScale">The scale of a drag frame, or null for a frame at the full resolution.</param>
    private sealed record RenderRequest(PreviewScene Scene, OrbitCamera Camera, int Width, int Height, double? DragScale, int Generation, CancellationTokenSource Cancellation);
}
