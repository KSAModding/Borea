using System.Numerics;

namespace Borea.Preview.Rendering;

/// <summary>An sRGB color with straight alpha, one byte per channel.</summary>
public readonly record struct Rgba8(byte R, byte G, byte B, byte A);

/// <summary>The settings of one <see cref="SoftwareRenderer.Render"/> call.</summary>
public sealed record RenderOptions
{
    /// <summary>One worker less than the processor count so the UI thread keeps a core, and at most 8.</summary>
    public static int DefaultMaxWorkers => Math.Clamp(Environment.ProcessorCount - 1, 1, 8);

    /// <summary>The color of every pixel that no draw covers. The default is fully transparent.</summary>
    public Rgba8 Background { get; init; }

    /// <summary>
    /// When true, a triangle that is clockwise on screen is not drawn, and the test is inverted for a draw
    /// whose transform mirrors it. Turn it off to see whether a mesh has the wrong winding.
    /// </summary>
    public bool CullBackFaces { get; init; } = true;

    /// <summary>The number of threads that set up and fill triangles. Values below 1 count as 1.</summary>
    public int MaxWorkers { get; init; } = DefaultMaxWorkers;

    /// <summary>
    /// The unit vector toward the sun in camera space, where +X is screen right, +Y is screen up and +Z
    /// points from the screen to the viewer. The default light comes from the upper left, behind the viewer,
    /// so the light follows the camera when the player turns the vehicle.
    /// </summary>
    public Vector3 SunDirection { get; init; } = Vector3.Normalize(new Vector3(-0.4f, 0.6f, 0.7f));

    public float SunIntensity { get; init; } = 0.85f;

    /// <summary>
    /// The light that reaches every face, also a face that turns away from the sun. The ambient occlusion
    /// of the material scales it, and only it, as the simple ambient of ThumbnailMesh.frag does.
    /// </summary>
    public float Ambient { get; init; } = 0.2f;

    /// <summary>
    /// The exposure of the Reinhard tone map, in the form of tonemapReinhardSimple in the game shader
    /// Tonemapping.glsl. The default makes a mid-gray diffuse under the full sun come out near mid-gray.
    /// Values that are not positive and finite count as 1.
    /// </summary>
    public float Exposure { get; init; } = 1.15f;

    /// <summary>
    /// The most bytes of vehicle frame positions and normals that one <see cref="SoftwareRenderer"/> keeps
    /// for its scene, 96 MiB. A draw that does not fit is transformed again on each frame.
    /// </summary>
    public long WorldCacheBytes { get; init; } = 96L * 1024 * 1024;

    /// <summary>
    /// When true, a draw whose bounding sphere shows smaller than one pixel is not drawn. A viewer turns it
    /// on for the frames of a drag, which have a low resolution and show for a short time only.
    /// </summary>
    public bool SkipSubPixelDraws { get; init; }

    /// <summary>The fraction of the full resolution that a viewer renders the frames of a drag at.</summary>
    public double DragScale { get; init; } = 0.5;

    /// <summary>The lower fraction that a viewer uses when a drag frame at <see cref="DragScale"/> was too slow.</summary>
    public double SlowDragScale { get; init; } = 0.25;

    /// <summary>A drag frame that takes longer than this many milliseconds makes the next drag frame use <see cref="SlowDragScale"/>.</summary>
    public double SlowDragThresholdMs { get; init; } = 60;

    /// <summary>
    /// The scale of the next drag frame after a drag frame at <paramref name="lastScale"/> that took
    /// <paramref name="lastFrameMs"/> milliseconds. A frame above <see cref="SlowDragThresholdMs"/> gives
    /// <see cref="SlowDragScale"/>. A frame at the slow scale goes back to <see cref="DragScale"/> only when it
    /// would stay below the threshold also with the pixel count of <see cref="DragScale"/>, so the scale does
    /// not change on every frame. A time that is not finite keeps the last scale.
    /// </summary>
    public double NextDragScale(double lastScale, double lastFrameMs)
    {
        if (!double.IsFinite(lastFrameMs))
        {
            return lastScale;
        }

        if (lastFrameMs > SlowDragThresholdMs)
        {
            return SlowDragScale;
        }

        if (lastScale >= DragScale || SlowDragScale <= 0)
        {
            return DragScale;
        }

        var pixelRatio = DragScale / lastScale * (DragScale / lastScale);
        return lastFrameMs * pixelRatio <= SlowDragThresholdMs ? DragScale : lastScale;
    }
}

/// <summary>What one render call did, so tests and the viewer can see the culling work.</summary>
/// <param name="DrawsVisited">The draws that passed the frustum test and went to triangle setup.</param>
/// <param name="PlaceholdersVisited">The placeholders that passed the frustum test.</param>
/// <param name="TrianglesDrawn">The screen triangles after clipping and culling, placeholders included.</param>
public readonly record struct RenderStats(int DrawsVisited, int PlaceholdersVisited, long TrianglesDrawn);
