using System.Numerics;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;

namespace Borea.Preview.Tests.Rendering;

public sealed class SoftwareRendererTests
{
    private static readonly Rgba8 Background = new(10, 20, 30, 255);
    private static readonly RenderOptions Options = new() { Background = Background };

    [Fact]
    public void ACube_CoversTheCenter_AndLeavesTheCornersAtTheBackground()
    {
        var scene = Scene(Box(Matrix4x4.Identity, new Vector3(0.8f)));
        var frame = Render(scene, Framed(scene.Bounds, 64, 48), 64, 48, Options, out var stats);

        Assert.Equal(1, stats.DrawsVisited);
        foreach (var (x, y) in new[] { (31, 23), (32, 23), (31, 24), (32, 24) })
        {
            Assert.Equal(255, Pixel(frame, x, y).A);
            Assert.True(frame.Depth[y * 64 + x] < 1f);
        }

        foreach (var (x, y) in new[] { (0, 0), (63, 0), (0, 47), (63, 47) })
        {
            Assert.Equal(Background, Pixel(frame, x, y));
            Assert.Equal(1f, frame.Depth[y * 64 + x]);
        }

        // Culling keeps the near faces, so the depth is the same as with every face drawn.
        var unculled = Render(scene, Framed(scene.Bounds, 64, 48), 64, 48, Options with { CullBackFaces = false }, out _);
        Assert.Equal(unculled.Depth, frame.Depth);
    }

    [Fact]
    public void TheNearerOfTwoOverlappingCubes_WinsOnDepth()
    {
        var towardEye = Vector3.Normalize(new OrbitCamera().Eye);
        var far = Box(Matrix4x4.Identity, new Vector3(0.9f, 0.1f, 0.1f));
        var near = Box(Matrix4x4.CreateTranslation(towardEye * 1.5f), new Vector3(0.1f, 0.1f, 0.9f));
        var camera = Framed(Scene(far, near).Bounds, 64, 64);

        var nearOnly = Pixel(Render(Scene(near), camera, 64, 64, Options, out _), 32, 32);
        var farOnly = Pixel(Render(Scene(far), camera, 64, 64, Options, out _), 32, 32);

        Assert.NotEqual(nearOnly, farOnly);
        Assert.Equal(nearOnly, Pixel(Render(Scene(far, near), camera, 64, 64, Options, out _), 32, 32));
        Assert.Equal(nearOnly, Pixel(Render(Scene(near, far), camera, 64, 64, Options, out _), 32, 32));
    }

    [Fact]
    public void AMirroredDraw_StillShowsItsOutside_WithCullingOn()
    {
        var plain = Scene(Box(Matrix4x4.Identity, new Vector3(0.8f)));
        var mirrored = Scene(Box(Matrix4x4.CreateScale(-1f, 1f, 1f), new Vector3(0.8f)));
        var camera = Framed(plain.Bounds, 48, 48);

        var plainFrame = Render(plain, camera, 48, 48, Options, out _);
        var mirroredFrame = Render(mirrored, camera, 48, 48, Options, out _);

        // A reversed winding test would cull the near faces and show the far inner faces at a larger depth.
        var center = 24 * 48 + 24;
        Assert.True(mirroredFrame.Depth[center] < 1f);
        Assert.Equal(plainFrame.Depth[center], mirroredFrame.Depth[center], 1e-5f);
    }

    [Fact]
    public void ADrawBehindTheCamera_IsNotVisited()
    {
        var camera = Framed(Scene(Box(Matrix4x4.Identity, Vector3.One)).Bounds, 32, 32);
        var behind = camera.Eye + Vector3.Normalize(camera.Eye - camera.Target) * 3f;
        var scene = Scene(Box(Matrix4x4.CreateTranslation(behind), Vector3.One));

        var frame = Render(scene, camera, 32, 32, Options, out var stats);

        Assert.Equal(0, stats.DrawsVisited);
        Assert.All(Enumerable.Range(0, 32 * 32), pixel => Assert.Equal(Background, Pixel(frame, pixel % 32, pixel / 32)));
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(40f, false)]
    public void OneAndFourWorkers_GiveTheSameFrame(float zoomSteps, bool cullBackFaces)
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var camera = Framed(scene.Bounds, 160, 100);
        camera.Zoom(zoomSteps);
        var options = Options with { CullBackFaces = cullBackFaces };

        var single = Render(scene, camera, 160, 100, options with { MaxWorkers = 1 }, out _);
        var four = Render(scene, camera, 160, 100, options with { MaxWorkers = 4 }, out _);

        Assert.Contains(single.Depth, depth => depth < 1f);
        Assert.Equal(single.Rgba, four.Rgba);
        Assert.Equal(single.Depth, four.Depth);
    }

    [Fact]
    public void ACancelledToken_Throws()
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var camera = Framed(scene.Bounds, 32, 32);

        Assert.Throws<OperationCanceledException>(() =>
            new SoftwareRenderer().Render(scene, camera, new FrameBuffer(32, 32), Options, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void TheStackedBoxes_PutTheTopBoxAboveTheBottomBox()
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var camera = Framed(scene.Bounds, 80, 120);

        var bottomRow = MeanCoveredRow(Render(Scene(scene.Draws[0]), camera, 80, 120, Options, out _));
        var topRow = MeanCoveredRow(Render(Scene(scene.Draws[2]), camera, 80, 120, Options, out _));

        Assert.True(topRow < bottomRow, $"The top box is at row {topRow} and the bottom box at row {bottomRow}.");
    }

    [Fact]
    public void ASecondFrameWithOnlyACameraChange_TransformsNoCachedDrawAgain()
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var camera = Framed(scene.Bounds, 64, 64);
        var renderer = new SoftwareRenderer();

        renderer.Render(scene, camera, new FrameBuffer(64, 64), Options, CancellationToken.None);
        var afterFirst = renderer.TransformedVertexCount;
        camera.Orbit(30f, 10f);
        renderer.Render(scene, camera, new FrameBuffer(64, 64), Options, CancellationToken.None);

        // Three boxes and one placeholder box of 24 vertices each.
        Assert.Equal(4 * 24, afterFirst);
        Assert.Equal(afterFirst, renderer.TransformedVertexCount);
    }

    [Fact]
    public void ASmallWorldCache_StaysUnderItsCap_AndGivesTheSameFrame()
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var camera = Framed(scene.Bounds, 96, 64);
        camera.Orbit(25f, 15f);

        // One box holds 24 positions and 24 normals of 12 bytes each, so the cap fits two of the four boxes.
        var capped = new SoftwareRenderer();
        var cappedOptions = Options with { WorldCacheBytes = 2 * 576 + 100 };
        var cappedFrames = new[] { new FrameBuffer(96, 64), new FrameBuffer(96, 64) };
        var uncappedFrames = new[] { new FrameBuffer(96, 64), new FrameBuffer(96, 64) };
        var uncapped = new SoftwareRenderer();
        for (var frame = 0; frame < 2; frame++)
        {
            capped.Render(scene, camera, cappedFrames[frame], cappedOptions, CancellationToken.None);
            uncapped.Render(scene, camera, uncappedFrames[frame], Options, CancellationToken.None);
            camera.Orbit(40f, 0f);
        }

        Assert.Equal(2 * 576, capped.CachedBytes);
        Assert.Equal(4 * 576, uncapped.CachedBytes);
        Assert.Equal(4 * 24 + 2 * 24, capped.TransformedVertexCount);
        for (var frame = 0; frame < 2; frame++)
        {
            Assert.Equal(uncappedFrames[frame].Rgba, cappedFrames[frame].Rgba);
            Assert.Equal(uncappedFrames[frame].Depth, cappedFrames[frame].Depth);
        }
    }

    [Fact]
    public void ASmallWorkBufferCap_BoundsTheRetainedBytes_AndGivesTheSameFrames()
    {
        var scene = PreviewTestScenes.StackedBoxes();
        var cameras = new[] { Framed(scene.Bounds, 96, 64), Framed(scene.Bounds, 96, 64) };
        cameras[0].Zoom(-10f);
        cameras[1].Zoom(20f);

        var uncapped = new SoftwareRenderer();
        var uncappedFrames = cameras.Select(camera =>
        {
            var frame = new FrameBuffer(96, 64);
            uncapped.Render(scene, camera, frame, Options, CancellationToken.None);
            return frame;
        }).ToArray();

        // Half of what the uncapped renderer keeps, so the trim must free some buffers but not all.
        var cap = uncapped.RetainedWorkBytes / 2;
        var capped = new SoftwareRenderer();
        for (var i = 0; i < cameras.Length; i++)
        {
            var frame = new FrameBuffer(96, 64);
            capped.Render(scene, cameras[i], frame, Options with { WorkBufferBytes = cap }, CancellationToken.None);

            Assert.InRange(capped.RetainedWorkBytes, 1, cap);
            Assert.Equal(uncappedFrames[i].Rgba, frame.Rgba);
            Assert.Equal(uncappedFrames[i].Depth, frame.Depth);
        }
    }

    [Fact]
    public void SkipSubPixelDraws_LeavesOutOnlyADrawBelowOnePixel()
    {
        var large = Box(Matrix4x4.Identity, new Vector3(0.8f));
        var tiny = Box(Matrix4x4.CreateScale(0.001f) * Matrix4x4.CreateTranslation(0.6f, 0f, 0f), new Vector3(0.2f));
        var scene = Scene(large, tiny);
        var camera = Framed(scene.Bounds, 32, 32);

        Render(scene, camera, 32, 32, Options, out var all);
        Render(scene, camera, 32, 32, Options with { SkipSubPixelDraws = true }, out var skipped);

        Assert.Equal(2, all.DrawsVisited);
        Assert.Equal(1, skipped.DrawsVisited);
    }

    private static PreviewDraw Box(Matrix4x4 transform, Vector3 color) =>
        new(PreviewTestScenes.CreateBox(), PreviewMaterial.Flat(color), transform);

    private static PreviewScene Scene(params PreviewDraw[] draws) => new(draws, []);

    private static OrbitCamera Framed(PreviewBounds bounds, int width, int height)
    {
        var camera = new OrbitCamera();
        camera.Frame(bounds, (float)width / height);
        return camera;
    }

    private static FrameBuffer Render(PreviewScene scene, OrbitCamera camera, int width, int height, RenderOptions options, out RenderStats stats)
    {
        var frame = new FrameBuffer(width, height);
        stats = new SoftwareRenderer().Render(scene, camera, frame, options, CancellationToken.None);
        return frame;
    }

    private static Rgba8 Pixel(FrameBuffer frame, int x, int y)
    {
        var offset = (y * frame.Width + x) * 4;
        return new Rgba8(frame.Rgba[offset], frame.Rgba[offset + 1], frame.Rgba[offset + 2], frame.Rgba[offset + 3]);
    }

    private static double MeanCoveredRow(FrameBuffer frame)
    {
        var rows = Enumerable.Range(0, frame.Depth.Length).Where(pixel => frame.Depth[pixel] < 1f).Select(pixel => pixel / frame.Width).ToList();
        Assert.NotEmpty(rows);
        return rows.Average();
    }
}
