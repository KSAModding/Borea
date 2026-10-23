using System.Numerics;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;

namespace Borea.Preview.Tests.Rendering;

public sealed class TexturedRenderTests
{
    /// <summary>A frame of 4n + 2 pixels puts the centers of pixels n and 3n + 1 exactly at u = 0.25 and 0.75.</summary>
    private const int Size = 66;
    private const int Near = 16;
    private const int Far = 49;
    private const int Center = 33;

    /// <summary>The sun along the view axis, so a quad that faces the camera gets the full light.</summary>
    private static readonly RenderOptions FullLight = new() { SunDirection = Vector3.UnitZ };

    [Fact]
    public void ATwoByTwoTexture_PutsEachTexelInItsScreenQuarter()
    {
        // Texel row 0 is the top row at v = 0, so red and green are on top and blue and white below.
        var texture = new PreviewImage(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]);
        var frame = RenderQuad(new PreviewMaterial(Vector3.Zero, texture, null, false), FullLight);

        var (topLeft, topRight) = (Pixel(frame, Near, Near), Pixel(frame, Far, Near));
        var (bottomLeft, bottomRight) = (Pixel(frame, Near, Far), Pixel(frame, Far, Far));
        Assert.True(topLeft.R > 150 && topLeft.G < 60 && topLeft.B < 60, $"top left {topLeft}");
        Assert.True(topRight.G > 150 && topRight.R < 60 && topRight.B < 60, $"top right {topRight}");
        Assert.True(bottomLeft.B > 150 && bottomLeft.R < 60 && bottomLeft.G < 60, $"bottom left {bottomLeft}");
        Assert.True(bottomRight.R > 150 && bottomRight.G > 150 && bottomRight.B > 150, $"bottom right {bottomRight}");
    }

    [Fact]
    public void AMidGrayDiffuse_UnderFullLight_StaysNearMidGray()
    {
        var pixel = Pixel(RenderQuad(new PreviewMaterial(Vector3.Zero, Solid(128, 128, 128), null, false), FullLight), Center, Center);

        // The sky and ground colors of the ambient are not quite gray, so the channels may differ a little.
        Assert.InRange(pixel.R, 118, 138);
        Assert.InRange(pixel.G - pixel.R, -3, 3);
        Assert.InRange(pixel.B - pixel.R, -3, 3);
    }

    [Fact]
    public void AMaterialWithoutDiffuse_IsShadedWithItsBaseColor()
    {
        var gray = MathF.Pow(128 / 255f, 2.2f);
        var textured = Pixel(RenderQuad(new PreviewMaterial(Vector3.Zero, Solid(128, 128, 128), null, false), FullLight), Center, Center);
        var flatGray = Pixel(RenderQuad(PreviewMaterial.Flat(new Vector3(gray)), FullLight), Center, Center);
        var flatRed = Pixel(RenderQuad(PreviewMaterial.Flat(new Vector3(0.6f, 0.1f, 0.02f)), FullLight), Center, Center);

        Assert.Equal(textured.R, flatGray.R, 1f);
        Assert.True(flatRed.R > flatRed.G && flatRed.G > flatRed.B, $"flat red {flatRed}");
    }

    [Fact]
    public void ZeroAmbientOcclusion_IsDarkerThanFull()
    {
        var diffuse = Solid(128, 128, 128);
        var occluded = Pixel(RenderQuad(new PreviewMaterial(Vector3.Zero, diffuse, Solid(0, 255, 0), false), new RenderOptions()), Center, Center);
        var open = Pixel(RenderQuad(new PreviewMaterial(Vector3.Zero, diffuse, Solid(255, 255, 0), false), new RenderOptions()), Center, Center);

        Assert.True(occluded.R + 8 < open.R, $"AO 0 gives {occluded.R} and AO 1 gives {open.R}.");
    }

    [Fact]
    public void AGlassMaterial_IsOpaqueWithTheDarkTint()
    {
        var color = new Vector3(0.9f, 0.8f, 0.7f);
        var glass = Pixel(RenderQuad(new PreviewMaterial(color, null, null, true), FullLight), Center, Center);
        var tinted = Pixel(RenderQuad(PreviewMaterial.Flat(color * SoftwareRenderer.GlassTint), FullLight), Center, Center);

        Assert.Equal(255, glass.A);
        Assert.Equal(tinted, glass);
    }

    [Fact]
    public void OneAndFourWorkers_GiveTheSameTexturedFrame()
    {
        var checker = new PreviewImage(2, 2, [230, 40, 30, 255, 20, 20, 20, 255, 20, 20, 20, 255, 240, 240, 240, 255]);
        var material = new PreviewMaterial(Vector3.One, checker, Solid(200, 90, 160), false);
        var boxes = PreviewTestScenes.StackedBoxes();
        var scene = new PreviewScene([.. boxes.Draws.Select(draw => draw with { Material = material })], boxes.Placeholders);
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, 160f / 100f);
        camera.Orbit(40f, 25f);

        var single = Render(scene, camera, 160, 100, new RenderOptions { MaxWorkers = 1 });
        var four = Render(scene, camera, 160, 100, new RenderOptions { MaxWorkers = 4 });

        Assert.Contains(single.Depth, depth => depth < 1f);
        Assert.Equal(single.Rgba, four.Rgba);
    }

    [Fact]
    public void AHighNormalMap_TiltsRedTowardPlusU_AndGreenTowardImageUp()
    {
        var fromPlusU = new RenderOptions { SunDirection = Vector3.Normalize(new Vector3(1f, 0f, 1f)), Quality = RenderQuality.High };
        var fromAbove = fromPlusU with { SunDirection = Vector3.Normalize(new Vector3(0f, 1f, 1f)) };
        int Brightness(byte red, byte green, RenderOptions options) => Pixel(RenderQuad(NormalMapped(Solid(red, green, 0)), options), Center, Center).R;

        var flat = Brightness(128, 128, fromPlusU);
        Assert.True(Brightness(200, 128, fromPlusU) > flat + 10, $"red high {Brightness(200, 128, fromPlusU)}, flat {flat}");
        Assert.True(Brightness(56, 128, fromPlusU) + 10 < flat, $"red low {Brightness(56, 128, fromPlusU)}, flat {flat}");

        var flatAbove = Brightness(128, 128, fromAbove);
        Assert.True(Brightness(128, 200, fromAbove) > flatAbove + 10, $"green high {Brightness(128, 200, fromAbove)}, flat {flatAbove}");
        Assert.True(Brightness(128, 56, fromAbove) + 10 < flatAbove, $"green low {Brightness(128, 56, fromAbove)}, flat {flatAbove}");

        // The Standard quality does not read the normal map.
        var standard = fromPlusU with { Quality = RenderQuality.Standard };
        Assert.Equal(Brightness(128, 128, standard), Brightness(200, 128, standard));
    }

    [Fact]
    public void OneAndFourWorkers_GiveTheSameHighFrame()
    {
        var checker = new PreviewImage(2, 2, [230, 40, 30, 255, 20, 20, 20, 255, 20, 20, 20, 255, 240, 240, 240, 255]);
        var bumps = new PreviewImage(2, 2, [200, 90, 0, 255, 60, 170, 0, 255, 128, 220, 0, 255, 30, 128, 0, 255]);
        var material = new PreviewMaterial(Vector3.One, checker, Solid(200, 90, 160), false) { Normal = bumps };
        var boxes = PreviewTestScenes.StackedBoxes();
        var scene = new PreviewScene([.. boxes.Draws.Select(draw => draw with { Material = material })], boxes.Placeholders);
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, 160f / 100f);
        camera.Orbit(40f, 25f);

        var single = Render(scene, camera, 160, 100, new RenderOptions { MaxWorkers = 1, Quality = RenderQuality.High });
        var four = Render(scene, camera, 160, 100, new RenderOptions { MaxWorkers = 4, Quality = RenderQuality.High });
        var standard = Render(scene, camera, 160, 100, new RenderOptions { MaxWorkers = 1 });

        Assert.Equal(single.Rgba, four.Rgba);
        Assert.NotEqual(standard.Rgba, single.Rgba);
    }

    [Fact]
    public void AMirroredDraw_UnderTheMirroredSun_ShowsTheSameRelief()
    {
        // Texel 0 at u = 0.25 tilts toward +u and image up, texel 1 at u = 0.75 toward -u and image down.
        var material = NormalMapped(new PreviewImage(2, 1, [200, 180, 0, 255, 56, 90, 0, 255]));
        var sun = Vector3.Normalize(new Vector3(0.8f, 0.4f, 1f));
        var plain = RenderQuad(material, new RenderOptions { SunDirection = sun, Quality = RenderQuality.High });
        var mirrored = RenderQuad(material, new RenderOptions { SunDirection = sun * new Vector3(-1f, 1f, 1f), Quality = RenderQuality.High }, Matrix4x4.CreateScale(-1f, 1f, 1f));

        // The mirror puts u = 0.25 at the right of the frame.
        var (plain0, plain1) = (Pixel(plain, Near, Center).R, Pixel(plain, Far, Center).R);
        var (mirrored0, mirrored1) = (Pixel(mirrored, Far, Center).R, Pixel(mirrored, Near, Center).R);
        Assert.True(plain0 > plain1 + 20, $"texel 0 gives {plain0} and texel 1 gives {plain1}");
        Assert.InRange(mirrored0 - plain0, -1, 1);
        Assert.InRange(mirrored1 - plain1, -1, 1);
    }

    [Fact]
    public void WithoutSun_AHighFaceTowardVehiclePlusX_IsBrighterThanOneTowardMinusX()
    {
        // Each face is seen from 60 degrees on its own side, so the two views are mirror images of each other.
        var material = new PreviewMaterial(Vector3.Zero, Solid(128, 128, 128), null, false);
        var options = new RenderOptions { SunIntensity = 0f, Quality = RenderQuality.High };
        var up = Pixel(RenderFaceAlongX(material, options, 1f), Center, Center);
        var down = Pixel(RenderFaceAlongX(material, options, -1f), Center, Center);

        Assert.True(up.R > down.R + 10, $"the face toward +X gives {up.R} and the face toward -X gives {down.R}");
    }

    [Fact]
    public void WithoutNormalMaps_HighAndStandard_GiveTheSameFrame()
    {
        // A smooth metal, a rough paint and a glass with a normal map, which glass never applies, and a placeholder.
        var checker = new PreviewImage(2, 2, [230, 40, 30, 255, 20, 20, 20, 255, 20, 20, 20, 255, 240, 240, 240, 255]);
        var bumps = new PreviewImage(2, 2, [200, 90, 0, 255, 60, 170, 0, 255, 128, 220, 0, 255, 30, 128, 0, 255]);
        PreviewMaterial[] materials =
        [
            new(Vector3.Zero, Solid(200, 200, 200), Solid(255, 20, 255), false),
            new(Vector3.Zero, checker, Solid(160, 230, 0), false),
            new(new Vector3(0.9f, 0.8f, 0.7f), null, null, true) { Normal = bumps },
        ];
        var boxes = PreviewTestScenes.StackedBoxes();
        var scene = new PreviewScene([.. boxes.Draws.Select((draw, i) => draw with { Material = materials[i] })], boxes.Placeholders);
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, 160f / 100f);
        camera.Orbit(40f, 25f);

        var standard = Render(scene, camera, 160, 100, new RenderOptions());
        var high = Render(scene, camera, 160, 100, new RenderOptions { Quality = RenderQuality.High });

        Assert.Contains(standard.Depth, depth => depth < 1f);
        Assert.Equal(standard.Rgba, high.Rgba);
    }

    /// <summary>
    /// Renders a square of 2 m in the vehicle plane x = 0 whose front face and normals point along
    /// <paramref name="side"/> times vehicle +X, seen from 60 degrees above that side.
    /// </summary>
    private static FrameBuffer RenderFaceAlongX(PreviewMaterial material, RenderOptions options, float side)
    {
        var mesh = new PreviewMesh(
            [0, -1, -1, 0, side, -side, 0, 1, 1, 0, -side, side],
            [side, 0, 0, side, 0, 0, side, 0, 0, side, 0, 0],
            [0, 0, 1, 0, 1, 1, 0, 1],
            [0, 1, 2, 0, 2, 3]);
        var scene = new PreviewScene([new PreviewDraw(mesh, material, Matrix4x4.Identity)], []);
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, 1f);
        camera.Orbit(0f, (side * 60f - 15f) * MathF.PI / 180f / OrbitCamera.RadiansPerPixel);
        return Render(scene, camera, Size, Size, options);
    }

    /// <summary>
    /// Renders a square that faces the camera and fills the square frame exactly, with v = 0 at its top
    /// edge and u = 0 at its left edge. Its transform is the inverse of the view, so it is built in camera
    /// space at the target distance.
    /// </summary>
    private static FrameBuffer RenderQuad(PreviewMaterial material, RenderOptions options, Matrix4x4? inCameraSpace = null)
    {
        var camera = new OrbitCamera();
        camera.Frame(PreviewBounds.FromCenter(Vector3.Zero, Vector3.One), 1f);
        Assert.True(Matrix4x4.Invert(camera.View, out var cameraToWorld));

        var half = camera.Distance * MathF.Tan(OrbitCamera.FieldOfViewY / 2f);
        var depth = -camera.Distance;
        var mesh = new PreviewMesh(
            [-half, -half, depth, half, -half, depth, half, half, depth, -half, half, depth],
            [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1],
            [0, 1, 1, 1, 1, 0, 0, 0],
            [0, 1, 2, 0, 2, 3]);
        var scene = new PreviewScene([new PreviewDraw(mesh, material, (inCameraSpace ?? Matrix4x4.Identity) * cameraToWorld)], []);
        return Render(scene, camera, Size, Size, options);
    }

    private static PreviewImage Solid(byte r, byte g, byte b) => new(1, 1, [r, g, b, 255]);

    private static PreviewMaterial NormalMapped(PreviewImage normal) => new(Vector3.Zero, Solid(128, 128, 128), null, false) { Normal = normal };

    private static FrameBuffer Render(PreviewScene scene, OrbitCamera camera, int width, int height, RenderOptions options)
    {
        var frame = new FrameBuffer(width, height);
        new SoftwareRenderer().Render(scene, camera, frame, options, CancellationToken.None);
        return frame;
    }

    private static Rgba8 Pixel(FrameBuffer frame, int x, int y)
    {
        var offset = (y * frame.Width + x) * 4;
        return new Rgba8(frame.Rgba[offset], frame.Rgba[offset + 1], frame.Rgba[offset + 2], frame.Rgba[offset + 3]);
    }
}
