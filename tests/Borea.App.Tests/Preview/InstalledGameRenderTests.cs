using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Borea.App.Tests.ViewModels;
using Borea.App.Tests.Views;
using Borea.App.Views.Preview;
using Borea.Composition;
using Borea.Core.Mods;
using Borea.Core.Preview;
using Borea.Core.Settings;
using Borea.Preview.Loading;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;
using Borea.Preview.Tests;
using Borea.Preview.Textures;
using Borea.Storage.Paths;
using Borea.Storage.Preview;
using Borea.Storage.Settings;
using Xunit.Abstractions;

namespace Borea.App.Tests.Preview;

/// <summary>
/// Renders stock vehicles of the installed game end to end, from a vehicle zip in the preview cache through the
/// <see cref="VehiclePreview"/> service, the scene builder and the software renderer, and writes the frames as PNG
/// files for a person to look at. Set BOREA_PREVIEW_GAME_DIR to the game folder to run it, BOREA_PREVIEW_OUT to the
/// folder for the PNG files, and BOREA_PREVIEW_VEHICLES to more vehicle folders, separated by the path separator.
/// </summary>
/// <remarks>
/// The winding of the game meshes is not in the decompiled source, so the test compares the depth of each frame with
/// back-face culling on and off. A mesh whose front faces the renderer takes for back faces loses its near side to
/// the culling, and the frame then shows the far inner faces, whose depth is larger.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class InstalledGameRenderTests(ITestOutputHelper output) : IDisposable
{
    private const int Width = 1024;
    private const int Height = 768;

    /// <summary>The same triangle gives the same depth in both frames, so only a different surface goes above this.</summary>
    private const float DepthEpsilon = 1e-5f;

    private const double MinEqualDepthShare = 0.95;

    private static readonly Rgba8 Background = new(58, 62, 70, 255);

    /// <summary>The views of each vehicle as (name, yaw drag, pitch drag) from the default angles, in degrees.</summary>
    private static readonly (string Name, float Yaw, float Pitch)[] Views = [("front", 0f, 0f), ("side", 120f, 0f), ("below", -150f, -45f)];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "borea-render-" + Guid.NewGuid().ToString("N"));

    [GameDirFact]
    public async Task TheDefaultVehicles_RenderEveryPart_WithTheExpectedTriangles_AndFrontFacesThatSurviveTheCulling()
    {
        var game = GameDirFactAttribute.GameDir!;
        var defaults = Path.Combine(game, "Content", "Core", "defaultvehicles");
        List<(string Folder, long? Triangles)> vehicles = [(Path.Combine(defaults, "Gemini7"), 137_000), (Path.Combine(defaults, "Rocket"), 445_000)];
        var extra = Environment.GetEnvironmentVariable("BOREA_PREVIEW_VEHICLES") ?? "";
        vehicles.AddRange(extra.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(folder => (folder, (long?)null)));
        var outFolder = Environment.GetEnvironmentVariable("BOREA_PREVIEW_OUT") is { Length: > 0 } named ? named : Path.Combine(_root, "png");
        Directory.CreateDirectory(outFolder);

        await new FileBoreaSettingsRepository(new GamePathProvider(gameDirectory: null, boreaRoot: _root)).SaveAsync(new BoreaSettings(game));
        using var services = await BoreaServices.BuildAsync(_root, new RefusingHandler(), new ViewModelHarness.FakeSpaceDock());
        var builder = new PreviewSceneBuilder(new PreviewTextureLoader(new AvaloniaRgbaImageDecoder()));
        var options = new PreviewLoadOptions { TextureTarget = 512 };
        output.WriteLine($"PNG folder: {outFolder}, ProcessorCount {Environment.ProcessorCount}");

        foreach (var (folder, triangles) in vehicles)
            await RenderVehicleAsync(services, builder, options, folder, triangles, outFolder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private async Task RenderVehicleAsync(BoreaServices services, PreviewSceneBuilder builder, PreviewLoadOptions options, string folder, long? triangles, string outFolder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var zip = ZipOf(folder);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(zip));
        var cached = Path.Combine(services.Paths.GetPreviewCacheFolder(), "archives", sha256 + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        await File.WriteAllBytesAsync(cached, zip);

        var watch = Stopwatch.StartNew();
        var plan = await services.VehiclePreview.PrepareAsync(Release(name, sha256, zip.Length), null);
        Assert.True(plan.State == VehiclePreviewPlanState.CanBuildLocally, $"{name}: {plan.State} {plan.Message}");
        using var result = await services.VehiclePreview.LoadAsync(plan, fetchVehicle: false, fetchMods: false);
        Assert.True(result.IsReady, $"{name}: {result.State} {result.Message}");
        var resolveMs = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        var (geometry, geometryNotices) = await builder.BuildGeometryAsync(result.Vehicle, result.Assets, options, CancellationToken.None);
        var geometryMs = watch.Elapsed.TotalMilliseconds;
        var entryPeak = VehiclePreview.EntryCacheBytesOf(result.Assets);
        watch.Restart();
        var (scene, textureNotices) = await builder.AddTexturesAsync(geometry, result.Vehicle, result.Assets, options, CancellationToken.None);
        var texturesMs = watch.Elapsed.TotalMilliseconds;
        entryPeak = Math.Max(entryPeak, VehiclePreview.EntryCacheBytesOf(result.Assets));

        output.WriteLine($"{name}: {result.Vehicle.PartCount} parts, {scene.Draws.Count} draws, {scene.TriangleCount} triangles, {result.Vehicle.Meshes.Count} meshes, {result.Vehicle.Materials.Count} materials, {result.Vehicle.SkippedInternalCount} internal models left out");
        output.WriteLine($"  load: prepare and resolve {resolveMs:0} ms, geometry {geometryMs:0} ms, textures {texturesMs:0} ms, entry cache peak {entryPeak} bytes");
        foreach (var notice in result.Notices.Concat(geometryNotices).Concat(textureNotices))
            output.WriteLine($"  notice {notice.Kind} {notice.Subject} {notice.Detail}");

        Assert.Empty(result.Vehicle.Placeholders);
        Assert.Empty(scene.Placeholders);
        Assert.DoesNotContain(textureNotices, notice => notice.Kind == PreviewNoticeKind.AssetFailed);
        if (triangles is { } expected)
            Assert.InRange(scene.TriangleCount, expected * 9 / 10, expected * 11 / 10);

        long worldPeak = 0;
        foreach (var workers in new[] { 1, 4 })
        {
            var (first, median, frameCache) = FrameTimes(scene, workers);
            worldPeak = Math.Max(worldPeak, frameCache);
            output.WriteLine($"  {workers} workers at {Width}x{Height}: first frame {first:0} ms, median of the next frames {median:0} ms");
        }

        var renderer = new SoftwareRenderer();
        var culled = new FrameBuffer(Width, Height);
        var unculled = new FrameBuffer(Width, Height);
        foreach (var (view, yaw, pitch) in Views)
        {
            var camera = new OrbitCamera();
            camera.Frame(scene.Bounds, (float)Width / Height);
            camera.Orbit(yaw * MathF.PI / 180f / OrbitCamera.RadiansPerPixel, pitch * MathF.PI / 180f / OrbitCamera.RadiansPerPixel);
            var culledStats = renderer.Render(scene, camera, culled, new RenderOptions { Background = Background }, CancellationToken.None);
            var unculledStats = renderer.Render(scene, camera, unculled, new RenderOptions { Background = Background, CullBackFaces = false }, CancellationToken.None);
            worldPeak = Math.Max(worldPeak, renderer.CachedBytes);

            var file = name.Replace(' ', '-');
            await WritePngAsync(culled, Path.Combine(outFolder, $"{file}-{view}-culled.png"));
            await WritePngAsync(unculled, Path.Combine(outFolder, $"{file}-{view}-unculled.png"));

            var (covered, equal) = CompareDepth(culled, unculled);
            var share = covered == 0 ? 0 : (double)equal / covered;
            output.WriteLine($"  {view}: {culledStats.TrianglesDrawn} of {unculledStats.TrianglesDrawn} triangles drawn with culling, {covered} pixels covered in both frames, {share:P2} with the same depth");
            Assert.True(share >= MinEqualDepthShare, $"{name} {view}: only {share:P2} of {covered} pixels keep their depth with culling on.");
        }

        output.WriteLine($"  world cache peak {worldPeak / 1048576.0:0.00} MiB");
    }

    /// <summary>The first frame fills the world cache, and the median comes from five more frames that each turn the camera a little.</summary>
    private static (double First, double Median, long CachedBytes) FrameTimes(PreviewScene scene, int workers)
    {
        var renderer = new SoftwareRenderer();
        var target = new FrameBuffer(Width, Height);
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, (float)Width / Height);
        var options = new RenderOptions { Background = Background, MaxWorkers = workers };

        var watch = Stopwatch.StartNew();
        renderer.Render(scene, camera, target, options, CancellationToken.None);
        var first = watch.Elapsed.TotalMilliseconds;
        var times = new double[5];
        for (var i = 0; i < times.Length; i++)
        {
            camera.Orbit(8f, 2f);
            watch.Restart();
            renderer.Render(scene, camera, target, options, CancellationToken.None);
            times[i] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        return (first, times[times.Length / 2], renderer.CachedBytes);
    }

    private static (int Covered, int Equal) CompareDepth(FrameBuffer culled, FrameBuffer unculled)
    {
        var covered = 0;
        var equal = 0;
        for (var i = 0; i < culled.Depth.Length; i++)
        {
            if (culled.Depth[i] < 1f && unculled.Depth[i] < 1f)
            {
                covered++;
                if (MathF.Abs(culled.Depth[i] - unculled.Depth[i]) <= DepthEpsilon)
                    equal++;
            }
        }

        return (covered, equal);
    }

    private static async Task WritePngAsync(FrameBuffer frame, string path)
    {
        var png = await HeadlessApp.RunAsync(() => Task.FromResult(new AvaloniaPngEncoder().Encode(frame)));
        await File.WriteAllBytesAsync(path, png);
    }

    /// <summary>A vehicle zip as a player shares it, with meta.toml and vehicle.xml of the folder at its root.</summary>
    private static byte[] ZipOf(string folder)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in new[] { "meta.toml", "vehicle.xml" })
                archive.CreateEntryFromFile(Path.Combine(folder, file), file);
        }

        return buffer.ToArray();
    }

    private static ModVersionMetadata Release(string name, string sha256, long size)
    {
        var id = new string([.. name.Where(char.IsAsciiLetterOrDigit)]);
        return new(1, id, ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.10.19.5638", 5638, new DownloadInfo($"https://example.com/{id}.zip", sha256, size, "application/zip"), null, [], ContentType.Vehicle, install: new InstallInfo(null, derived: false, folder: name));
    }

    /// <summary>Fails every request, because a local preview of a cached vehicle must not reach a host.</summary>
    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException($"The test sends no request, and {request.RequestUri} was asked for.");
    }
}
