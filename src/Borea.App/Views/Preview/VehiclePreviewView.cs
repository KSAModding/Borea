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
using Avalonia.Threading;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;
using PreviewRenderOptions = Borea.Preview.Rendering.RenderOptions;

namespace Borea.App.Views.Preview;

/// <summary>
/// Shows a <see cref="PreviewScene"/> with the controls of the orbit camera of the game editor. A left or a
/// right drag turns the view, with or without Shift, and so do the arrow keys. A middle drag zooms, away from
/// the vehicle for a drag down and closer for a drag up. The wheel moves the target up and down the vehicle
/// axis, and the wheel with Shift zooms. Q and E move the target up and down the axis too, and W and S zoom.
/// A move of the target scales with the height that the view shows at the target, so it feels the same for a
/// small capsule and a tall rocket. F, Home and a double click with the left button frame the whole vehicle
/// again. The software renderer draws each frame on a background task into a bitmap that this control stretches
/// over its bounds, and it draws only when the scene, the size or the camera changes.
/// While a drag holds the pointer, the frames render at <see cref="PreviewRenderOptions.DragScale"/> of the
/// full resolution, or at <see cref="PreviewRenderOptions.SlowDragScale"/> when <see cref="DragScaleController"/>
/// finds the drag frames too slow. Each new drag starts at the normal drag scale again. When a full frame shows
/// and no input follows for <see cref="RefineDelay"/>, the view renders the same camera once more at
/// <see cref="RenderQuality.High"/>, and any input cancels that frame at once.
/// </summary>
public sealed class VehiclePreviewView : Control
{
    /// <summary>How far one arrow key press turns the camera, 10 degrees.</summary>
    internal const float KeyStepPixels = 10f * MathF.PI / 180f / OrbitCamera.RadiansPerPixel;

    /// <summary>How far one wheel notch or one press of Q or E moves the target, as a fraction of the height that the view shows at the target.</summary>
    internal const float AxisStepFraction = 0.1f;

    /// <summary>
    /// The zoom steps for each pixel that a middle drag moves down. The game multiplies the distance by 1.1 to the
    /// power of 0.1 for each pixel, and these steps give the same factor, so the value is negative and a drag down
    /// moves away.
    /// </summary>
    internal static readonly float ZoomStepsPerDragPixel = 0.1f * MathF.Log(1.1f) / MathF.Log(OrbitCamera.ZoomStepFactor);

    public static readonly StyledProperty<PreviewScene?> SceneProperty =
        AvaloniaProperty.Register<VehiclePreviewView, PreviewScene?>(nameof(Scene));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<VehiclePreviewView>();

    /// <summary>A count that a view model raises to ask for <see cref="ResetView"/>. Each change frames the scene again.</summary>
    public static readonly StyledProperty<int> ResetCountProperty =
        AvaloniaProperty.Register<VehiclePreviewView, int>(nameof(ResetCount));

    // The renderer clears to transparent and the Background brush shows through, so the bitmap uses straight alpha.
    private static readonly PreviewRenderOptions Options = new();

    // A drag frame shows for a short time only, so it leaves out the parts that are smaller than one of its pixels.
    private static readonly PreviewRenderOptions DragOptions = Options with { SkipSubPixelDraws = true };

    // The view renders this level only for a camera at rest, because a frame costs more for each covered pixel.
    private static readonly PreviewRenderOptions HighOptions = Options with { Quality = RenderQuality.High };

    // The pitch in drag pixels stays inside the clamp of OrbitCamera.Orbit, so a drag back responds at once.
    private static readonly float MinOrbitY = (-OrbitCamera.MaxPitch - OrbitCamera.DefaultPitch) / OrbitCamera.RadiansPerPixel;
    private static readonly float MaxOrbitY = (OrbitCamera.MaxPitch - OrbitCamera.DefaultPitch) / OrbitCamera.RadiansPerPixel;

    // The zoom steps stay inside the distance clamp of OrbitCamera.Zoom for the same reason.
    private static readonly float MinZoomSteps = MathF.Log(OrbitCamera.MaxZoomFactor) / MathF.Log(OrbitCamera.ZoomStepFactor);
    private static readonly float MaxZoomSteps = MathF.Log(OrbitCamera.MinZoomFactor) / MathF.Log(OrbitCamera.ZoomStepFactor);

    // The renderer keeps the vertices of the scene between frames, and only one frame renders at a time.
    private readonly SoftwareRenderer _renderer = new();
    private OrbitCamera _camera = new();
    private readonly DragScaleController _dragScales = new(Options);
    private FrameBuffer? _fullFrame;
    private FrameBuffer? _dragFrame;
    private float _orbitX;
    private float _orbitY;
    private float _zoomSteps;

    // The target offset along +X from the framed center. RequestRender keeps it inside the reach of the camera.
    private float _axisOffset;
    private Point? _dragFrom;
    private bool _dragZooms;
    private int _leftClicks;
    private WriteableBitmap? _bitmap;
    private RenderRequest? _queued;
    private bool _rendering;
    private int _generation;
    private CancellationTokenSource _cancellation = new();
    private IDisposable? _refineTimer;
    private CancellationTokenSource? _refine;

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

    public int ResetCount
    {
        get => GetValue(ResetCountProperty);
        set => SetValue(ResetCountProperty, value);
    }

    internal float CameraDistance => _camera.Distance;

    internal float CameraYaw => _camera.Yaw;

    internal float CameraAxisOffset => _camera.AxisOffset;

    internal float CameraAxisReach => _camera.AxisReach;

    /// <summary>
    /// The number of Standard frames that showed. A High frame only refines the frame that shows, so it counts in
    /// <see cref="HighFramesShown"/> and not here.
    /// </summary>
    internal int FramesShown { get; private set; }

    internal int HighFramesShown { get; private set; }

    internal RenderQuality ShownQuality { get; private set; }

    /// <summary>The number of drag frames whose time went to the <see cref="DragScaleController"/>.</summary>
    internal int DragFramesRecorded { get; private set; }

    /// <summary>How long a full frame shows without input before the view renders it again at High quality.</summary>
    internal TimeSpan RefineDelay { get; set; } = TimeSpan.FromMilliseconds(180);

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
        _axisOffset = 0;
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
            var old = change.GetOldValue<PreviewScene?>();
            var scene = change.GetNewValue<PreviewScene?>();

            // The wheel moves the camera only for a scene, so without one a page that scrolls around the view gets the wheel.
            SmoothScroll.SetOwnsWheel(this, scene is not null);
            StartGeneration();
            ReleaseRenderState();

            // A scene with the same bounds is the same vehicle with other materials, such as the textured scene
            // that follows the flat one. It keeps the pose of the player and the frame that shows until its own
            // frame is ready, so the view does not jump back or flash.
            if (old is null || scene is null || old.Bounds != scene.Bounds)
            {
                ReleaseBitmap();
                _dragScales.Reset();
                _orbitX = 0;
                _orbitY = 0;
                _zoomSteps = 0;
                _axisOffset = 0;
            }

            // When a High frame of the same vehicle shows, the new scene renders at High at once. The old High frame
            // then stays until the new one replaces it, and the view does not step back to Standard and refine again.
            var keepHigh = ShownQuality == RenderQuality.High && _dragFrom is null;
            RequestRender(full: _dragFrom is null, high: keepHigh);
        }
        else if (change.Property == ResetCountProperty)
        {
            ResetView();
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
        var properties = point.Properties;
        var left = properties.IsLeftButtonPressed;

        // Avalonia counts the presses of all buttons in one click count, so a left press right after a right click
        // has a count of 2. The view counts the left presses in a run itself, and a press of another button ends the run.
        _leftClicks = !left ? 0 : e.ClickCount > 1 && _leftClicks > 0 ? _leftClicks + 1 : 1;

        // A right or a middle press without a scene goes on to the page, which scrolls with a middle press.
        if (!left && (Scene is null || (!properties.IsRightButtonPressed && !properties.IsMiddleButtonPressed)))
            return;

        // A press can start a drag that moves the camera, so the High frame of the old camera stops before it.
        CancelRefine();
        Focus(NavigationMethod.Pointer);
        e.Handled = true;
        if (_leftClicks == 2)
        {
            _dragFrom = null;
            ResetView();
            return;
        }

        _dragFrom = point.Position;
        _dragZooms = !left && !properties.IsRightButtonPressed;
        _dragScales.Reset();
        e.Pointer.Capture(this);

        // The press can stop the High frame of a new scene, which then needs a frame of its own before the drag moves.
        if (Scene is not null && ShownScene is not null && ShownScene != Scene)
            RequestRender(full: false);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from || e.Pointer.Captured != this)
            return;

        var position = e.GetPosition(this);
        _dragFrom = position;
        var (dx, dy) = ((float)(position.X - from.X), (float)(position.Y - from.Y));
        if (_dragZooms)
            Zoom(dy * ZoomStepsPerDragPixel);
        else
            Orbit(dx, dy, full: false);

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // The base raises a context request for a right release that is not handled, so the release is handled first.
        if (_dragFrom is not null)
        {
            // Clearing the drag first lets the capture-lost handler see that the drag already ended.
            _dragFrom = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            RequestRender(full: true);
        }
        else if (e.InitialPressMouseButton == MouseButton.Right && Scene is not null)
        {
            e.Handled = true;
        }

        base.OnPointerReleased(e);
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
        if (Scene is null)
            return;

        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Some systems turn the wheel with Shift into a sideways wheel, so a delta only on X counts too.
            Zoom((float)(e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X));
        }
        else
        {
            MoveAlongAxis((float)e.Delta.Y);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Scene is not null && e.KeyModifiers == KeyModifiers.None && MoveByKey(e))
        {
            e.Handled = true;
            return;
        }

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

    /// <summary>
    /// Applies Home and the letter keys of the camera. The letters are physical keys, as in the game, so they keep
    /// their place on any keyboard layout. Returns false for any other key.
    /// </summary>
    private bool MoveByKey(KeyEventArgs e)
    {
        if (e.Key == Key.Home)
        {
            ResetView();
            return true;
        }

        switch (e.PhysicalKey)
        {
            case PhysicalKey.W:
                Zoom(1f);
                return true;
            case PhysicalKey.S:
                Zoom(-1f);
                return true;
            case PhysicalKey.Q:
                MoveAlongAxis(1f);
                return true;
            case PhysicalKey.E:
                MoveAlongAxis(-1f);
                return true;
            case PhysicalKey.F:
                ResetView();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Moves the target up the vehicle axis by <paramref name="steps"/> of <see cref="AxisStepFraction"/>, or down for negative steps.</summary>
    private void MoveAlongAxis(float steps)
    {
        _axisOffset += steps * _camera.ViewHeight * AxisStepFraction;
        RequestRender(full: _dragFrom is null);
    }

    private void Zoom(float steps)
    {
        _zoomSteps = Math.Clamp(_zoomSteps + steps, MinZoomSteps, MaxZoomSteps);
        RequestRender(full: _dragFrom is null);
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
    /// With <paramref name="high"/>, the full frame renders at High quality without a Standard frame first.
    /// </summary>
    private void RequestRender(bool full, bool high = false)
    {
        CancelRefine();
        var size = Bounds.Size;
        var aspect = size.Width > 0 && size.Height > 0 ? (float)(size.Width / size.Height) : 1f;
        var scene = Scene;
        var camera = new OrbitCamera();
        camera.Frame(scene?.Bounds ?? PreviewBounds.Empty, aspect);
        camera.Orbit(_orbitX, _orbitY);
        camera.Zoom(_zoomSteps);
        camera.SetAxisOffset(_axisOffset);

        // An offset past an end of the vehicle stopped at that end, so a move back responds at once.
        _axisOffset = camera.AxisOffset;
        _camera = camera;

        if (VisualRoot is null || scene is null || size.Width <= 0 || size.Height <= 0)
            return;

        var frameScale = full ? 1 : _dragScales.Scale;
        var scale = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * frameScale;
        var width = Math.Clamp((int)Math.Ceiling(size.Width * scale), 1, FrameBuffer.MaxSize);
        var height = Math.Clamp((int)Math.Ceiling(size.Height * scale), 1, FrameBuffer.MaxSize);
        var request = new RenderRequest(scene, camera, width, height, full ? null : frameScale, _generation, _cancellation);
        if (high && full)
            QueueRefine(request);
        else
            Queue(request);
    }

    private void Queue(RenderRequest request)
    {
        _queued = request;
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
                    // A High frame stops on its own token, which a new generation also cancels because it is linked to it.
                    var high = request.Quality == RenderQuality.High;
                    var token = high ? request.RefineToken : request.Cancellation.Token;
                    var reused = TakeFrameBuffer(request);
                    var options = high ? HighOptions : request.DragScale is null ? Options : DragOptions;
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

                // A Standard frame of an older camera still shows, so a drag that keeps moving keeps showing frames. A
                // High frame only refines the frame that shows, so it shows only while no input followed it.
                if (frame is not null && request.Generation == _generation && (request.Quality != RenderQuality.High || IsRefineCurrent(request)))
                {
                    Show(frame, request.Scene, request.Quality);
                    KeepFrameBuffer(request, frame);
                    if (request.DragScale is { } dragScale)
                    {
                        _dragScales.Record(dragScale, elapsedMs);
                        DragFramesRecorded++;
                    }
                    else if (request.Quality == RenderQuality.Standard && _queued is null && _dragFrom is null)
                    {
                        StartRefine(request);
                    }
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

    private bool IsRefineCurrent(RenderRequest request) =>
        request.Camera == _camera && _refine is { } refine && refine.Token == request.RefineToken && !refine.IsCancellationRequested;

    /// <summary>Waits <see cref="RefineDelay"/> after the full frame of <paramref name="shown"/> and then queues it at High quality.</summary>
    private void StartRefine(RenderRequest shown)
    {
        CancelRefine();
        if (VisualRoot is null)
            return;

        _refineTimer = DispatcherTimer.RunOnce(
            () =>
            {
                _refineTimer = null;
                if (VisualRoot is null || shown.Generation != _generation || shown.Camera != _camera)
                    return;

                QueueRefine(shown);
            },
            RefineDelay);
    }

    /// <summary>Queues <paramref name="request"/> at High quality with a new refine source.</summary>
    private void QueueRefine(RenderRequest request)
    {
        // The refine source is linked to the generation source, so a scene change or a detach also stops it.
        _refine = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
        Queue(request with { Quality = RenderQuality.High, RefineToken = _refine.Token });
    }

    /// <summary>
    /// Stops the timer and the High frame. The view is the only owner of the refine source, and only this method
    /// cancels and disposes it, so a render that still holds its token sees a cancelled token and never a call on a
    /// disposed source.
    /// </summary>
    private void CancelRefine()
    {
        _refineTimer?.Dispose();
        _refineTimer = null;
        var refine = _refine;
        _refine = null;
        if (refine is not null)
        {
            refine.Cancel();
            refine.Dispose();
        }
    }

    private void Show(FrameBuffer frame, PreviewScene scene, RenderQuality quality)
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
        ShownQuality = quality;
        if (quality == RenderQuality.High)
            HighFramesShown++;
        else
            FramesShown++;
        InvalidateVisual();
    }

    /// <summary>Makes every running and queued frame stale, so a frame of the old scene or of a detached view never shows.</summary>
    private void StartGeneration()
    {
        CancelRefine();
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
        ShownQuality = RenderQuality.Standard;
        InvalidateVisual();
    }

    /// <param name="DragScale">The scale of a drag frame, or null for a frame at the full resolution.</param>
    /// <param name="Cancellation">The source of the generation. A render of an older generation disposes it.</param>
    /// <param name="RefineToken">The token of the refine source that a High frame stops on. The render never disposes that source.</param>
    private sealed record RenderRequest(
        PreviewScene Scene,
        OrbitCamera Camera,
        int Width,
        int Height,
        double? DragScale,
        int Generation,
        CancellationTokenSource Cancellation,
        RenderQuality Quality = RenderQuality.Standard,
        CancellationToken RefineToken = default);
}
