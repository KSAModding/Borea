namespace Borea.Preview.Rendering;

/// <summary>
/// Chooses the scale of the next drag frame from the times of the last drag frames. It takes no time of
/// its own, so a test can feed it any frame times. A drag starts at <see cref="RenderOptions.DragScale"/>
/// and goes to <see cref="RenderOptions.SlowDragScale"/> only after two frames in a row above
/// <see cref="RenderOptions.SlowDragThresholdMs"/>, so one pause of the garbage collector does not change
/// it. A frame at the slow scale goes back when it took at most half the threshold, or when it saved less
/// than <see cref="MinSaving"/> of the time of the last frame at the normal scale. In the second case the
/// slow scale does not help with this scene, because the triangle setup and not the pixel count bounds the
/// frame, so the controller keeps the normal scale until <see cref="Reset"/>.
/// </summary>
public sealed class DragScaleController
{
    /// <summary>The fraction of the frame time that the slow scale must save to stay in use.</summary>
    public const double MinSaving = 0.3;

    private const int SlowFramesToSwitch = 2;

    private readonly RenderOptions _options;
    private int _slowFrames;
    private double _normalScaleMs = double.NaN;
    private bool _slowScaleHelps = true;

    public DragScaleController(RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        Scale = options.DragScale;
    }

    /// <summary>The scale that the next drag frame renders at.</summary>
    public double Scale { get; private set; }

    /// <summary>Starts a new drag at the normal drag scale and forgets what the last drag measured.</summary>
    public void Reset()
    {
        Scale = _options.DragScale;
        _slowFrames = 0;
        _normalScaleMs = double.NaN;
        _slowScaleHelps = true;
    }

    /// <summary>
    /// Takes the time of one drag frame that rendered at <paramref name="scale"/>. A time that is not
    /// finite or is negative changes nothing.
    /// </summary>
    public void Record(double scale, double frameMs)
    {
        if (!double.IsFinite(frameMs) || frameMs < 0)
        {
            return;
        }

        var threshold = _options.SlowDragThresholdMs;
        if (scale >= _options.DragScale)
        {
            _normalScaleMs = frameMs;
            _slowFrames = frameMs > threshold ? _slowFrames + 1 : 0;
            var slowScaleUsable = _options.SlowDragScale > 0 && _options.SlowDragScale < _options.DragScale;
            if (_slowFrames >= SlowFramesToSwitch && _slowScaleHelps && slowScaleUsable)
            {
                Scale = _options.SlowDragScale;
                _slowFrames = 0;
            }

            return;
        }

        if (frameMs <= threshold / 2)
        {
            Scale = _options.DragScale;
        }
        else if (double.IsFinite(_normalScaleMs) && frameMs > (1 - MinSaving) * _normalScaleMs)
        {
            Scale = _options.DragScale;
            _slowScaleHelps = false;
        }
    }
}
