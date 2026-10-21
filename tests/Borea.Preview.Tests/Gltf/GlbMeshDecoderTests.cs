using System.Text.Json.Nodes;
using Borea.Core.Preview;
using Borea.Preview.Gltf;
using Borea.Preview.Scene;

namespace Borea.Preview.Tests.Gltf;

public sealed class GlbMeshDecoderTests
{
    private static readonly float[] QuadPositions = [0, 0, 0, 1, 0, 0, 1, 2, 0, 0, 2, 0];
    private static readonly float[] QuadNormals = [0, 0, 1, 0, 1, 0, 1, 0, 0, 0.6f, 0.8f, 0];
    private static readonly float[] QuadUvs = [0, 0, 1, 0, 1, 1, 0.25f, 0.75f];
    private static readonly uint[] QuadIndices = [0, 1, 2, 0, 2, 3];

    // The BIN layout of the interleaved quad: 4 unused bytes, the vertex view at 4 with a stride of 32
    // (position, normal, UV), then the index view at 132, whose accessor starts 4 bytes in, and the
    // positions of a second mesh at 148.
    private const int VertexViewOffset = 4;
    private const int IndexViewOffset = 132;
    private const int IndexAccessorOffset = 4;
    private const int SecondMeshOffset = 148;

    [Fact]
    public void InterleavedVerticesWithShortIndices_DecodeExactly()
    {
        var mesh = Decode(InterleavedQuad(), 0);

        Assert.Equal(QuadPositions, mesh.Positions);
        Assert.Equal(QuadNormals, mesh.Normals);
        Assert.Equal(QuadUvs, mesh.Uvs);
        Assert.Equal(QuadIndices, mesh.Indices);
    }

    [Theory]
    [InlineData(GlbTestWriter.UnsignedByteComponent)]
    [InlineData(GlbTestWriter.UnsignedIntComponent)]
    public void ByteAndIntIndices_Decode(int indexComponent)
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var indexBytes = GlbTestWriter.Indices(indexComponent, 2, 0, 1);
        var views = new JsonArray { View(0, 36), View(36, indexBytes.Length) };
        var accessors = new JsonArray
        {
            Accessor(0, GlbTestWriter.FloatComponent, 3, "VEC3"),
            Accessor(1, indexComponent, 3, "SCALAR"),
        };
        var primitive = new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = 0 }, ["indices"] = 1 };
        byte[] bin = [.. GlbTestWriter.Floats(positions), .. indexBytes];

        var mesh = Decode(GlbTestWriter.Pack(Document(views, accessors, bin.Length, primitive), bin), 0);

        // The mesh has no normals, so the flat-normal pass lays the corners out in index order.
        Assert.Equal([0, 1, 0, 0, 0, 0, 1, 0, 0], mesh.Positions);
        Assert.Equal([0u, 1u, 2u], mesh.Indices);
    }

    [Fact]
    public void ANonIndexedMesh_Decodes()
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var views = new JsonArray { View(0, 36) };
        var accessors = new JsonArray { Accessor(0, GlbTestWriter.FloatComponent, 3, "VEC3") };
        var primitive = new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = 0 } };
        var bin = GlbTestWriter.Floats(positions);

        var mesh = Decode(GlbTestWriter.Pack(Document(views, accessors, bin.Length, primitive), bin), 0);

        Assert.Equal(positions, mesh.Positions);
        Assert.Equal([0u, 1u, 2u], mesh.Indices);
    }

    [Fact]
    public void MissingNormals_GiveUnitFlatNormalsForEachFace()
    {
        // Two faces that share the edge from vertex 0 to vertex 1, one facing +Z and one facing +Y.
        float[] positions = [0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0, 2];
        var indexBytes = GlbTestWriter.Indices(GlbTestWriter.UnsignedShortComponent, 0, 1, 2, 0, 3, 1);
        var views = new JsonArray { View(0, 48), View(48, indexBytes.Length) };
        var accessors = new JsonArray
        {
            Accessor(0, GlbTestWriter.FloatComponent, 4, "VEC3"),
            Accessor(1, GlbTestWriter.UnsignedShortComponent, 6, "SCALAR"),
        };
        var primitive = new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = 0 }, ["indices"] = 1 };
        byte[] bin = [.. GlbTestWriter.Floats(positions), .. indexBytes];

        var mesh = Decode(GlbTestWriter.Pack(Document(views, accessors, bin.Length, primitive), bin), 0);

        Assert.Equal(6, mesh.VertexCount);
        Assert.Equal([0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1, 0, 0, 1, 0], mesh.Normals);
        Assert.Equal([0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 2, 2, 0, 0], mesh.Positions);
        Assert.Equal([0u, 1u, 2u, 3u, 4u, 5u], mesh.Indices);
    }

    [Fact]
    public void ANodeTranslation_DoesNotMoveTheVertices()
    {
        var file = InterleavedQuad(json =>
        {
            json["nodes"] = new JsonArray { new JsonObject { ["mesh"] = 0, ["translation"] = new JsonArray(10, 20, 30) } };
            json["scenes"] = new JsonArray { new JsonObject { ["nodes"] = new JsonArray(0) } };
            json["scene"] = 0;
        });

        Assert.Equal(QuadPositions, Decode(file, 0).Positions);
    }

    public static TheoryData<string, Action<JsonObject, byte[]>> BrokenFiles => new()
    {
        { "accessor past the end of its view", (json, _) => json["accessors"]![0]!["count"] = 5 },
        { "view past the end of the buffer", (json, _) => json["bufferViews"]![0]!["byteLength"] = 1000 },
        { "sparse accessor", (json, _) => json["accessors"]![1]!["sparse"] = new JsonObject { ["count"] = 1 } },
        { "required extension", (json, _) => json["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression") },
        { "line mode", (json, _) => json["meshes"]![0]!["primitives"]![0]!["mode"] = 1 },
        { "external buffer", (json, _) => json["buffers"]![0]!["uri"] = "quad.bin" },
        { "index out of range", (_, bin) => bin[IndexViewOffset + IndexAccessorOffset] = 9 },
    };

    [Theory]
    [MemberData(nameof(BrokenFiles))]
    public void ABrokenFile_ThrowsGlbFormatException(string name, Action<JsonObject, byte[]> breakFile)
    {
        var file = InterleavedQuad(breakFile);

        var exception = Record.Exception(() => Decode(file, 0));

        Assert.True(exception is GlbFormatException, $"{name}: {exception?.GetType().Name ?? "no exception"}");
    }

    [Fact]
    public void Decoding_ReadsOnlyTheBinHeaderAndTheRangesOfTheMesh()
    {
        var file = InterleavedQuad();
        using var stream = new RecordingStream(file);
        var container = GlbContainer.Read(stream);
        stream.Reads.Clear();

        var mesh = GlbMeshDecoder.Decode(stream, container, 0);

        var binOffset = container.BinOffset;
        (long Start, long End)[] allowed =
        [
            (binOffset - 8, binOffset),
            (binOffset + VertexViewOffset, binOffset + IndexViewOffset),
            (binOffset + IndexViewOffset + IndexAccessorOffset, binOffset + SecondMeshOffset),
        ];
        Assert.Equal(2, mesh.TriangleCount);
        Assert.NotEmpty(stream.Reads);
        Assert.All(stream.Reads, read => Assert.Contains(allowed, range => read.Start >= range.Start && read.End <= range.End));
    }

    [GameDirFact]
    public void MeshZeroOfEveryCoreMeshAtlas_HasTriangles()
    {
        var files = Directory.GetFiles(Path.Combine(GameDirFactAttribute.GameDir!, "Content", "Core"), "Core*_MeshAtlas.glb", SearchOption.AllDirectories);

        Assert.NotEmpty(files);
        Assert.All(files, path =>
        {
            using var stream = File.OpenRead(path);
            var mesh = GlbMeshDecoder.Decode(stream, GlbContainer.Read(stream), 0);
            Assert.True(mesh.TriangleCount > 0, path);
        });
    }

    private static PreviewMesh Decode(byte[] file, int meshIndex)
    {
        using var stream = new MemoryStream(file, writable: false);
        return GlbMeshDecoder.Decode(stream, GlbContainer.Read(stream), meshIndex);
    }

    /// <summary>
    /// Two meshes: mesh 0 is the quad with interleaved position, normal and UV and 16-bit indices, and mesh 1 is a
    /// non-indexed triangle whose positions follow in their own view.
    /// </summary>
    private static byte[] InterleavedQuad(Action<JsonObject, byte[]>? breakFile = null)
    {
        var bin = new byte[SecondMeshOffset + 36];
        for (var vertex = 0; vertex < 4; vertex++)
        {
            var offset = VertexViewOffset + (vertex * 32);
            GlbTestWriter.Floats(QuadPositions[(vertex * 3)..((vertex * 3) + 3)]).CopyTo(bin, offset);
            GlbTestWriter.Floats(QuadNormals[(vertex * 3)..((vertex * 3) + 3)]).CopyTo(bin, offset + 12);
            GlbTestWriter.Floats(QuadUvs[(vertex * 2)..((vertex * 2) + 2)]).CopyTo(bin, offset + 24);
        }

        GlbTestWriter.Indices(GlbTestWriter.UnsignedShortComponent, QuadIndices).CopyTo(bin, IndexViewOffset + IndexAccessorOffset);
        GlbTestWriter.Floats(0, 0, 0, 1, 0, 0, 0, 1, 0).CopyTo(bin, SecondMeshOffset);

        var vertexView = View(VertexViewOffset, 128);
        vertexView["byteStride"] = 32;
        var views = new JsonArray { vertexView, View(IndexViewOffset, 16), View(SecondMeshOffset, 36) };
        var accessors = new JsonArray
        {
            Accessor(0, GlbTestWriter.FloatComponent, 4, "VEC3"),
            Accessor(0, GlbTestWriter.FloatComponent, 4, "vec3", 12),
            Accessor(0, GlbTestWriter.FloatComponent, 4, "VEC2", 24),
            Accessor(1, GlbTestWriter.UnsignedShortComponent, 6, "SCALAR", IndexAccessorOffset),
            Accessor(2, GlbTestWriter.FloatComponent, 3, "VEC3"),
        };
        var quad = new JsonObject
        {
            ["attributes"] = new JsonObject { ["POSITION"] = 0, ["NORMAL"] = 1, ["TEXCOORD_0"] = 2 },
            ["indices"] = 3,
            ["mode"] = 4,
        };
        var json = Document(views, accessors, bin.Length, quad);
        json["meshes"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "Triangle",
            ["primitives"] = new JsonArray { new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = 4 } } },
        });

        breakFile?.Invoke(json, bin);
        return GlbTestWriter.Pack(json, bin);
    }

    private static byte[] InterleavedQuad(Action<JsonObject> changeJson) => InterleavedQuad((json, _) => changeJson(json));

    private static JsonObject Document(JsonArray views, JsonArray accessors, int binLength, JsonObject primitive) => new()
    {
        ["asset"] = new JsonObject { ["version"] = "2.0" },
        ["buffers"] = new JsonArray { new JsonObject { ["byteLength"] = binLength } },
        ["bufferViews"] = views,
        ["accessors"] = accessors,
        ["meshes"] = new JsonArray { new JsonObject { ["name"] = "Mesh", ["primitives"] = new JsonArray { primitive } } },
    };

    private static JsonObject View(int offset, int length) => new() { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = length };

    private static JsonObject Accessor(int view, int componentType, int count, string type, int offset = 0) => new()
    {
        ["bufferView"] = view,
        ["byteOffset"] = offset,
        ["componentType"] = componentType,
        ["count"] = count,
        ["type"] = type,
    };

    /// <summary>A seekable stream that records the byte range of every read.</summary>
    private sealed class RecordingStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public List<(long Start, long End)> Reads { get; } = [];

        public override int Read(byte[] buffer, int offset, int count) => Record(base.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Record(Position, base.Read(buffer));

        private int Record(int read) => Record(Position - read, read);

        private int Record(long start, int read)
        {
            if (read > 0)
            {
                Reads.Add((start, start + read));
            }

            return read;
        }
    }
}
