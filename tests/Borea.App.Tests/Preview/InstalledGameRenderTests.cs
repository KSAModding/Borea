using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Borea.App.Tests.ViewModels;
using Borea.App.Tests.Views;
using Borea.App.Views.Preview;
using Borea.Composition;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Core.Planning;
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
/// BOREA_PREVIEW_MODS names mod zips or mod folders the same way. Each one goes into the archive cache as a zip, a
/// folder with itself as the top-level directory, and an offline repository names a release of it, so that every
/// vehicle of BOREA_PREVIEW_VEHICLES needs these mods and reads them in place from the cache as before an install.
/// </summary>
/// <remarks>
/// The game draws parts with back-face culling and counter-clockwise front faces (PartModelRenderer), but whether a
/// triangle is counter-clockwise on screen depends on the projection, so the test checks the winding of this renderer
/// directly. It compares the depth of each frame with back-face culling on and off. If the renderer takes the front
/// faces of a mesh for back faces, the culling removes the near side of the mesh, and the frame then shows the far
/// inner faces, which have a larger depth.
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
        var mods = PathList("BOREA_PREVIEW_MODS").Select(ModArchiveOf).ToList();
        List<(string Folder, long? Triangles, IReadOnlyList<ModDependency> Needs)> vehicles = [(Path.Combine(defaults, "Gemini7"), 137_000, []), (Path.Combine(defaults, "Rocket"), 445_000, [])];
        IReadOnlyList<ModDependency> needs = [.. mods.Select(mod => new ModDependency(mod.Id, ModDependencyKind.Required))];
        vehicles.AddRange(PathList("BOREA_PREVIEW_VEHICLES").Select(folder => (folder, (long?)null, needs)));
        var outFolder = Environment.GetEnvironmentVariable("BOREA_PREVIEW_OUT") is { Length: > 0 } named ? named : Path.Combine(_root, "png");
        Directory.CreateDirectory(outFolder);

        await new FileBoreaSettingsRepository(new GamePathProvider(gameDirectory: null, boreaRoot: _root)).SaveAsync(new BoreaSettings(game));
        var offline = new ViewModelHarness.FakeSpaceDock();
        using var services = await BoreaServices.BuildAsync(
            _root,
            new RefusingHandler(),
            offline,
            new ViewModelHarness.FakeInstallCandidates(),
            wrapInstallPlanner: mods.Count == 0 ? null : inner => new OfflineReleasesPlanner(inner, offline));
        foreach (var mod in mods)
        {
            var sha256 = await CacheArchiveAsync(services, mod.Zip);
            offline.Releases.Add(new ModVersionMetadata(1, mod.Id, ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.10.19.5638", 5638, new DownloadInfo($"https://example.com/{mod.Id}.zip", sha256, mod.Zip.Length, "application/zip"), mod.InstallSizeBytes, []));
            output.WriteLine($"Mod {mod.Id}: zip {mod.Zip.Length} bytes, {mod.InstallSizeBytes} bytes unpacked, sha256 {sha256}");
        }

        var builder = new PreviewSceneBuilder(new PreviewTextureLoader(new AvaloniaRgbaImageDecoder()));
        var options = new PreviewLoadOptions { TextureTarget = 512 };
        output.WriteLine($"PNG folder: {outFolder}, ProcessorCount {Environment.ProcessorCount}");

        foreach (var (folder, triangles, vehicleNeeds) in vehicles)
            await RenderVehicleAsync(services, builder, options, folder, triangles, vehicleNeeds, outFolder);
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

    private async Task RenderVehicleAsync(BoreaServices services, PreviewSceneBuilder builder, PreviewLoadOptions options, string folder, long? triangles, IReadOnlyList<ModDependency> needs, string outFolder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var zip = ZipOf(folder);
        var sha256 = await CacheArchiveAsync(services, zip);

        var watch = Stopwatch.StartNew();
        var plan = await services.VehiclePreview.PrepareAsync(Release(name, sha256, zip.Length, needs), null);
        Assert.True(plan.State == VehiclePreviewPlanState.CanBuildLocally, $"{name}: {plan.State} {plan.Message}");
        Assert.Empty(plan.MissingMods);
        using var result = await services.VehiclePreview.LoadAsync(plan, fetchVehicle: false, fetchMods: false);
        Assert.True(result.IsReady, $"{name}: {result.State} {result.Message}");
        var resolveMs = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        var (geometry, geometryNotices) = await builder.BuildGeometryAsync(result.Vehicle, result.Assets, options, CancellationToken.None);
        var geometryMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        // The headless session registers the Avalonia platform, which decodes PNG and JPEG textures, only while one of
        // its dispatches runs, and the App has it for its whole run.
        var (scene, textureNotices) = await HeadlessApp.RunAsync(() => builder.AddTexturesAsync(geometry, result.Vehicle, result.Assets, options, CancellationToken.None));
        var texturesMs = watch.Elapsed.TotalMilliseconds;

        output.WriteLine($"{name}: {result.Vehicle.PartCount} parts, {scene.Draws.Count} draws, {scene.TriangleCount} triangles, {result.Vehicle.Meshes.Count} meshes, {result.Vehicle.Materials.Count} materials, {result.Vehicle.SkippedInternalCount} internal models left out");
        output.WriteLine($"  load: prepare and resolve {resolveMs:0} ms, geometry {geometryMs:0} ms, textures {texturesMs:0} ms, managed heap {GC.GetTotalMemory(forceFullCollection: true) / 1048576.0:0.0} MiB with the result open");
        foreach (var notice in result.Notices.Concat(geometryNotices).Concat(textureNotices))
            output.WriteLine($"  notice {notice.Kind} {notice.Subject} {notice.Detail}");

        Assert.Empty(result.Vehicle.Placeholders);
        Assert.Empty(scene.Placeholders);
        if (triangles is { } expected)
        {
            // A stock vehicle needs only Core, so any notice means that a part, mesh or texture took a fallback.
            Assert.Empty(result.Notices);
            Assert.Empty(geometryNotices);
            Assert.Empty(textureNotices);
            Assert.InRange(scene.TriangleCount, expected * 9 / 10, expected * 11 / 10);
        }
        else
        {
            Assert.DoesNotContain(result.Notices, notice => notice.Kind is PreviewNoticeKind.AssetFailed or PreviewNoticeKind.MissingSource or PreviewNoticeKind.PlannerFallback);
            Assert.DoesNotContain(geometryNotices.Concat(textureNotices), notice => notice.Kind == PreviewNoticeKind.AssetFailed);
        }

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

    private static IEnumerable<string> PathList(string variable) =>
        (Environment.GetEnvironmentVariable(variable) ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Puts the archive into the preview cache under its hash, as a fetch leaves it, and gives the hash.</summary>
    private static async Task<string> CacheArchiveAsync(BoreaServices services, byte[] zip)
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(zip));
        var cached = Path.Combine(services.Paths.GetPreviewCacheFolder(), "archives", sha256 + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        await File.WriteAllBytesAsync(cached, zip);
        return sha256;
    }

    /// <summary>
    /// A mod zip as it is, or a mod folder zipped below a top-level directory of its own name, with the id from the
    /// name in its mod.toml and the unpacked size as the sum of the entry lengths.
    /// </summary>
    private static (string Id, byte[] Zip, long InstallSizeBytes) ModArchiveOf(string path)
    {
        byte[] zip;
        if (File.Exists(path))
        {
            zip = File.ReadAllBytes(path);
        }
        else
        {
            var top = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            using var buffer = new MemoryStream();
            using (var created = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    created.CreateEntryFromFile(file, top + "/" + Path.GetRelativePath(path, file).Replace('\\', '/'));
            }

            zip = buffer.ToArray();
        }

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var definition = archive.Entries.Where(entry => Regex.IsMatch(entry.FullName.Replace('\\', '/'), @"^([^/]+/)?mod\.toml$", RegexOptions.IgnoreCase)).MinBy(entry => entry.FullName.Length)
            ?? throw new InvalidOperationException($"{path} has no mod.toml at its root or in one top-level directory.");
        using var reader = new StreamReader(definition.Open());
        var id = Regex.Match(reader.ReadToEnd(), @"^\s*name\s*=\s*""([^""]+)""", RegexOptions.Multiline) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"The mod.toml of {path} has no name.");
        return (id, zip, archive.Entries.Sum(entry => entry.Length));
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

    private static ModVersionMetadata Release(string name, string sha256, long size, IReadOnlyList<ModDependency> needs)
    {
        var id = new string([.. name.Where(char.IsAsciiLetterOrDigit)]);
        return new(1, id, ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.10.19.5638", 5638, new DownloadInfo($"https://example.com/{id}.zip", sha256, size, "application/zip"), null, needs, ContentType.Vehicle, install: new InstallInfo(null, derived: false, folder: name));
    }

    /// <summary>
    /// Plans every request over the given repository, which stands in for the index snapshots on disk. The test root
    /// has none, and the releases of BOREA_PREVIEW_MODS are not listed in any index.
    /// </summary>
    private sealed class OfflineReleasesPlanner(IInstallPlanner inner, IModRepository releases) : IInstallPlanner
    {
        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default) =>
            inner.PlanAsync(request with { Repository = releases }, cancellationToken);
    }

    /// <summary>Fails every request, because a local preview of a cached vehicle must not reach a host.</summary>
    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException($"The test sends no request, and {request.RequestUri} was asked for.");
    }
}
