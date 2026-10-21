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
}

/// <summary>What one render call did, so tests and the viewer can see the culling work.</summary>
/// <param name="DrawsVisited">The draws that passed the frustum test and went to triangle setup.</param>
/// <param name="PlaceholdersVisited">The placeholders that passed the frustum test.</param>
/// <param name="TrianglesDrawn">The screen triangles after clipping and culling, placeholders included.</param>
public readonly record struct RenderStats(int DrawsVisited, int PlaceholdersVisited, long TrianglesDrawn);
