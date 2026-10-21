using System.Diagnostics;
using System.Numerics;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;
using Xunit.Abstractions;

namespace Borea.Preview.Tests.Rendering;

/// <summary>
/// Measures the frame time of synthetic scenes of the size of a large vehicle. The numbers are printed and
/// not asserted, because they depend on the machine. Set BOREA_PREVIEW_BENCH=1 to run them.
/// </summary>
public sealed class RenderBenchmarkTests(ITestOutputHelper output)
{
    private const int Width = 1024;
    private const int Height = 768;
    private const int MeasuredFrames = 9;

    [BenchFact]
    public void RocketSizedScene_400kTrianglesIn500Draws() => Measure("400k triangles, 500 draws", BuildScene(drawCount: 500, trianglesPerDraw: 800));

    [BenchFact]
    public void BudgetSizedScene_1MTrianglesIn500Draws() => Measure("1M triangles, 500 draws", BuildScene(drawCount: 500, trianglesPerDraw: 2000));

    private void Measure(string name, PreviewScene scene)
    {
        output.WriteLine($"{name}: {scene.TriangleCount} triangles at {Width}x{Height}, ProcessorCount {Environment.ProcessorCount}");
        foreach (var workers in new[] { Environment.ProcessorCount, 4 })
        {
            var full = new RenderOptions { MaxWorkers = workers };
            var drag = full with { SkipSubPixelDraws = true };
            var defaults = new RenderOptions();
            output.WriteLine($"  {workers} threads:");
            output.WriteLine($"    full   {Median(scene, full, 1.0)}");
            output.WriteLine($"    drag {defaults.DragScale:0.00} {Median(scene, drag, defaults.DragScale)}");
            output.WriteLine($"    drag {defaults.SlowDragScale:0.00} {Median(scene, drag, defaults.SlowDragScale)}");
        }
    }

    /// <summary>
    /// The first frame fills the cache of the renderer, one full turn follows, and the measured frames after
    /// it turn the camera a little each, as a drag does. The text gives the first frame, the median of the
    /// measured frames and the bytes that one measured frame allocates on all threads.
    /// </summary>
    private static string Median(PreviewScene scene, RenderOptions options, double scale)
    {
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        var renderer = new SoftwareRenderer();
        var target = new FrameBuffer(width, height);
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, (float)width / height);

        var watch = Stopwatch.StartNew();
        renderer.Render(scene, camera, target, options, CancellationToken.None);
        var first = watch.Elapsed.TotalMilliseconds;

        // One full turn lets the buffers of the renderer grow to what every angle needs.
        var turnPixels = 2f * MathF.PI / OrbitCamera.RadiansPerPixel;
        for (var i = 0; i < 16; i++)
        {
            camera.Orbit(turnPixels / 16f, i < 8 ? 4f : -4f);
            renderer.Render(scene, camera, target, options, CancellationToken.None);
        }

        var times = new double[MeasuredFrames];
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < times.Length; i++)
        {
            camera.Orbit(6f, 2f);
            watch.Restart();
            renderer.Render(scene, camera, target, options, CancellationToken.None);
            times[i] = watch.Elapsed.TotalMilliseconds;
        }

        var perFrame = (GC.GetTotalAllocatedBytes(precise: true) - allocated) / times.Length;
        Array.Sort(times);
        return $"{width}x{height}: first {first,6:0.0} ms, median {times[times.Length / 2],6:0.0} ms, {perFrame / 1024.0,8:0.0} KiB allocated per frame";
    }

    /// <summary>
    /// A column of 50 distinct tubes, each drawn 10 times around the axis like boosters and tanks, with one
    /// textured material, so the meshes are shared between draws as atlas meshes are in a real vehicle.
    /// </summary>
    private static PreviewScene BuildScene(int drawCount, int trianglesPerDraw)
    {
        var texture = Checker(512);
        var material = new PreviewMaterial(Vector3.One, texture, Solid(255, 140, 40), false);
        var meshes = Enumerable.Range(0, 50).Select(i => Tube(trianglesPerDraw, 0.5f + 0.02f * i)).ToArray();
        var draws = new List<PreviewDraw>(drawCount);
        for (var i = 0; i < drawCount; i++)
        {
            var level = i / 10;
            var slot = i % 10;
            var angle = slot * MathF.Tau / 10f;
            var offset = slot == 0 ? Vector3.Zero : new Vector3(0f, MathF.Cos(angle), MathF.Sin(angle)) * 1.6f;
            var transform = Matrix4x4.CreateScale(slot == 0 ? 1.4f : 0.5f) * Matrix4x4.CreateTranslation(new Vector3(level * 1.1f, 0f, 0f) + offset);
            draws.Add(new PreviewDraw(meshes[level % meshes.Length], material, transform));
        }

        return new PreviewScene(draws, []);
    }

    /// <summary>A closed tube along +X of length 1 with about <paramref name="triangles"/> side triangles.</summary>
    private static PreviewMesh Tube(int triangles, float radius)
    {
        var segments = 40;
        var rings = Math.Max(1, triangles / (2 * segments));
        var positions = new List<float>();
        var normals = new List<float>();
        var uvs = new List<float>();
        var indices = new List<uint>();
        for (var ring = 0; ring <= rings; ring++)
        {
            for (var segment = 0; segment <= segments; segment++)
            {
                var angle = segment * MathF.Tau / segments;
                var normal = new Vector3(0f, MathF.Cos(angle), MathF.Sin(angle));
                var position = new Vector3((float)ring / rings, 0f, 0f) + normal * radius;
                positions.AddRange([position.X, position.Y, position.Z]);
                normals.AddRange([normal.X, normal.Y, normal.Z]);
                uvs.AddRange([(float)segment / segments, (float)ring / rings]);
            }
        }

        var stride = (uint)(segments + 1);
        for (var ring = 0u; ring < rings; ring++)
        {
            for (var segment = 0u; segment < segments; segment++)
            {
                var a = ring * stride + segment;
                var b = a + stride;
                indices.AddRange([a, a + 1, b + 1, a, b + 1, b]);
            }
        }

        return new PreviewMesh([.. positions], [.. normals], [.. uvs], [.. indices]);
    }

    private static PreviewImage Checker(int size)
    {
        var rgba = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = (byte)(((x / 32) + (y / 32)) % 2 == 0 ? 220 : 60);
                var offset = (y * size + x) * 4;
                (rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]) = (value, value, value, 255);
            }
        }

        return new PreviewImage(size, size, rgba);
    }

    private static PreviewImage Solid(byte r, byte g, byte b) => new(1, 1, [r, g, b, 255]);

    /// <summary>A fact that runs only when BOREA_PREVIEW_BENCH is 1, because a benchmark is slow and its result depends on the machine.</summary>
    private sealed class BenchFactAttribute : FactAttribute
    {
        public BenchFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("BOREA_PREVIEW_BENCH") != "1")
            {
                Skip = "Set BOREA_PREVIEW_BENCH=1 to run the render benchmark.";
            }
        }
    }
}
