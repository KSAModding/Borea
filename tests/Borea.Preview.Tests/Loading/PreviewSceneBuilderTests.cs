using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json.Nodes;
using Borea.Core.Preview;
using Borea.Preview.Loading;
using Borea.Preview.Scene;
using Borea.Preview.Tests.Gltf;
using Borea.Preview.Tests.Textures;
using Borea.Preview.Textures;

namespace Borea.Preview.Tests.Loading;

public sealed class PreviewSceneBuilderTests
{
    private static readonly PreviewAssetRef GoodGlb = new("Core", "Meshes/Good.glb");
    private static readonly PreviewAssetRef BadGlb = new("Core", "Meshes/Bad.glb");
    private static readonly PreviewAssetRef GoodTexture = new("Core", "Textures/Good.ktx2");
    private static readonly PreviewAssetRef MissingTexture = new("Core", "Textures/Missing.ktx2");
    private static readonly PreviewLoadOptions Options = new(16);

    [Fact]
    public async Task TwoDrawsOfTheSameMesh_ShareOnePreviewMesh()
    {
        var vehicle = Vehicle([Mesh(GoodGlb, 0)], [], [Draw(0, -1), Draw(0, -1, 3f)]);

        var (scene, notices) = await Builder().BuildGeometryAsync(vehicle, Source(), Options, CancellationToken.None);

        Assert.Empty(notices);
        Assert.Equal(2, scene.Draws.Count);
        Assert.Same(scene.Draws[0].Mesh, scene.Draws[1].Mesh);
    }

    [Fact]
    public async Task AGoodMeshACorruptMeshAndAMissingTexture_EachFailByThemselves()
    {
        var vehicle = Vehicle(
            [Mesh(GoodGlb, 0), Mesh(BadGlb, 0)],
            [new PreviewMaterialRef("MissingMat", MissingTexture, null, false), new PreviewMaterialRef("GoodMat", GoodTexture, null, false)],
            [Draw(0, 0), Draw(0, 1, 2f), Draw(1, 1, 4f)]);
        var source = Source();
        var builder = Builder();

        var geometry = await builder.BuildGeometryAsync(vehicle, source, Options, CancellationToken.None);
        var textured = await builder.AddTexturesAsync(geometry.Scene, vehicle, source, Options, CancellationToken.None);

        Assert.Equal(2, textured.Scene.Draws.Count);
        var flat = textured.Scene.Draws[0].Material;
        Assert.Null(flat.Diffuse);
        Assert.Equal(PreviewSceneBuilder.BaseColorOf("MissingMat"), flat.BaseColor);
        Assert.NotNull(textured.Scene.Draws[1].Material.Diffuse);
        Assert.Equal(PreviewPlaceholderReason.AssetFailed, Assert.Single(textured.Scene.Placeholders).Reason);
        Assert.Contains(geometry.Notices, notice => notice.Kind == PreviewNoticeKind.AssetFailed && notice.Subject == "Core/Meshes/Bad.glb");
        Assert.Contains(textured.Notices, notice => notice.Kind == PreviewNoticeKind.AssetFailed && notice.Subject == "Core/Textures/Missing.ktx2");
    }

    [Theory]
    [InlineData(1, 1_000_000)]
    [InlineData(4, 1_000_000)]
    [InlineData(16, 2_000_000)]
    public void TheTriangleBudget_ComesFromTheProcessorCount(int processorCount, int triangles)
    {
        Assert.Equal(triangles, new PreviewLoadOptions(processorCount).MaxTriangles);
    }

    [Fact]
    public async Task AboveTheTriangleBudget_TheLargestDrawsBecomeBudgetBoxes()
    {
        var vehicle = Vehicle([Mesh(GoodGlb, 0)], [], [Draw(0, -1), Draw(0, -1, 3f)]);
        var options = Options with { MaxTriangles = 3 };

        var (scene, notices) = await Builder().BuildGeometryAsync(vehicle, Source(), options, CancellationToken.None);

        Assert.True(scene.TriangleCount <= options.MaxTriangles);
        var box = Assert.Single(scene.Placeholders);
        Assert.Equal(PreviewPlaceholderReason.Budget, box.Reason);

        // The box takes the min and max that the POSITION accessor declares, which is the quad from (0, 0) to (1, 2).
        Assert.Equal(new Vector3(0.5f, 1f, 0f), box.HalfSize);
        Assert.Contains(notices, notice => notice.Kind == PreviewNoticeKind.Budget);
    }

    [Fact]
    public async Task EachGlbFile_IsOpenedOncePerBuild_AndASecondBuildComesFromTheMeshCache()
    {
        var vehicle = Vehicle([Mesh(GoodGlb, 0), Mesh(GoodGlb, 1)], [], [Draw(0, -1), Draw(1, -1, 2f), Draw(0, -1, 4f)]);
        var builder = Builder();
        var first = Source();

        await builder.BuildGeometryAsync(vehicle, first, Options, CancellationToken.None);
        var second = Source();
        var (scene, _) = await builder.BuildGeometryAsync(vehicle, second, Options, CancellationToken.None);

        Assert.Equal(1, first.Opens.GetValueOrDefault(GoodGlb.Path));
        Assert.Equal(0, first.HeadOpens);
        Assert.Empty(second.Opens);
        Assert.Equal(3, scene.Draws.Count);
    }

    [Fact]
    public async Task ACancelAfterTheGeometryStage_StopsTheTextureStage()
    {
        var vehicle = Vehicle([Mesh(GoodGlb, 0)], [new PreviewMaterialRef("GoodMat", GoodTexture, null, false)], [Draw(0, 0)]);
        var source = Source();
        var builder = Builder();
        using var cancellation = new CancellationTokenSource();

        var (scene, _) = await builder.BuildGeometryAsync(vehicle, source, Options, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.AddTexturesAsync(scene, vehicle, source, Options, cancellation.Token));
        Assert.Equal(0, source.Opens.GetValueOrDefault(GoodTexture.Path));
    }

    [Fact]
    public async Task ThePlaceholdersOfTheVehicle_KeepTheirReasonAndModId()
    {
        var missing = new ResolvedPlaceholder(Matrix4x4.CreateTranslation(5f, 0f, 0f), new Vector3(1f), "MissingTank", PlaceholderReason.ModNotLoaded, "SomeMod");
        var vehicle = Vehicle([], [], []) with { Placeholders = [missing] };

        var (scene, _) = await Builder().BuildGeometryAsync(vehicle, Source(), Options, CancellationToken.None);

        var box = Assert.Single(scene.Placeholders);
        Assert.Equal((PreviewPlaceholderReason.ModNotLoaded, "SomeMod", "MissingTank"), (box.Reason, box.ModId, box.MissingId));
    }

    private static PreviewSceneBuilder Builder() => new(new PreviewTextureLoader(null));

    private static CountingSource Source() => new()
    {
        Files =
        {
            [GoodGlb.Path] = TwoQuadMeshes(),
            [BadGlb.Path] = "not a GLB file at all"u8.ToArray(),
            [GoodTexture.Path] = Ktx2TestWriter.Write(37, 2, 2, [new byte[16]]),
        },
    };

    private static ResolvedVehicle Vehicle(PreviewMeshRef[] meshes, PreviewMaterialRef[] materials, ResolvedDraw[] draws) =>
        new(meshes, materials, draws, [], [], draws.Length, 0);

    private static PreviewMeshRef Mesh(PreviewAssetRef file, int index) => new(file, index, $"Mesh{index}");

    private static ResolvedDraw Draw(int mesh, int material, float x = 0f) =>
        new(mesh, material, Matrix4x4.CreateTranslation(x, 0f, 0f), "TestPart", PreviewModelKind.Model);

    /// <summary>
    /// A GLB with two meshes that share one quad of two triangles from (0, 0, 0) to (1, 2, 0), with UVs, 16-bit
    /// indices and the min and max that glTF asks for on POSITION.
    /// </summary>
    private static byte[] TwoQuadMeshes()
    {
        byte[] bin =
        [
            .. GlbTestWriter.Floats(0, 0, 0, 1, 0, 0, 1, 2, 0, 0, 2, 0),
            .. GlbTestWriter.Floats(0, 0, 1, 0, 1, 1, 0, 1),
            .. GlbTestWriter.Indices(GlbTestWriter.UnsignedShortComponent, 0, 1, 2, 0, 2, 3),
        ];
        var primitive = new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = 0, ["TEXCOORD_0"] = 1 }, ["indices"] = 2 };
        var json = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["buffers"] = new JsonArray { new JsonObject { ["byteLength"] = bin.Length } },
            ["bufferViews"] = new JsonArray { View(0, 48), View(48, 32), View(80, 12) },
            ["accessors"] = new JsonArray
            {
                new JsonObject
                {
                    ["bufferView"] = 0, ["componentType"] = GlbTestWriter.FloatComponent, ["count"] = 4, ["type"] = "VEC3",
                    ["min"] = new JsonArray(0, 0, 0), ["max"] = new JsonArray(1, 2, 0),
                },
                new JsonObject { ["bufferView"] = 1, ["componentType"] = GlbTestWriter.FloatComponent, ["count"] = 4, ["type"] = "VEC2" },
                new JsonObject { ["bufferView"] = 2, ["componentType"] = GlbTestWriter.UnsignedShortComponent, ["count"] = 6, ["type"] = "SCALAR" },
            },
            ["meshes"] = new JsonArray
            {
                new JsonObject { ["name"] = "Mesh0", ["primitives"] = new JsonArray { primitive.DeepClone() } },
                new JsonObject { ["name"] = "Mesh1", ["primitives"] = new JsonArray { primitive.DeepClone() } },
            },
        };
        return GlbTestWriter.Pack(json, bin);
    }

    private static JsonObject View(int offset, int length) => new() { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = length };

    /// <summary>An in-memory source that counts each open of a path, so a test can see what a build reads.</summary>
    private sealed class CountingSource : IPreviewAssetSource
    {
        private int _headOpens;

        public Dictionary<string, byte[]> Files { get; } = [];

        public ConcurrentDictionary<string, int> Opens { get; } = new();

        public int HeadOpens => _headOpens;

        public Stream? TryOpenRead(PreviewAssetRef asset)
        {
            Opens.AddOrUpdate(asset.Path, 1, (_, count) => count + 1);
            return Files.TryGetValue(asset.Path, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
        }

        public Stream? TryOpenHead(PreviewAssetRef asset)
        {
            Interlocked.Increment(ref _headOpens);
            return Files.TryGetValue(asset.Path, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
        }

        public string GetCacheKey(PreviewAssetRef asset) => $"{asset.Source}/{asset.Path}";

        public void Dispose()
        {
        }
    }
}
