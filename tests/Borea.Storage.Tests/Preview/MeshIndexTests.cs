using Borea.Core.Preview;
using Borea.Storage.Preview.Ksa;
using Borea.Storage.Preview.Sources;

namespace Borea.Storage.Tests.Preview;

public sealed class MeshIndexTests : IDisposable
{
    private readonly CatalogTestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    [Fact]
    public void AMeshId_FindsTheGltfNameInAnyCase_TheFirstAtlasWins_AndHostOnlyIsLeftOut()
    {
        const string assets = """
            <MeshAtlas Path="Meshes/Host.glb" HostOnly="true" />
            <MeshAtlas Path="Meshes/A.glb" />
            <MeshAtlas Path="Meshes/B.glb" />
            <MeshFile Id="Box" Path="Meshes/box.glb" />
            <Part Id="P"><PartModel Id="m"><Mesh Id="abc" /></PartModel></Part>
            """;
        var core = _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(assets)));
        _mods.Write("Core", "Meshes/Host.glb", Glb("ABC", "HostOnlyMesh"));
        _mods.Write("Core", "Meshes/A.glb", Glb("Other", "ABC"));
        _mods.Write("Core", "Meshes/B.glb", Glb("abc", "OnlyInB"));

        var catalog = PartCatalog.Build([core], CancellationToken.None);
        var index = MeshIndex.Build([core], catalog.MeshFiles, CancellationToken.None);

        Assert.True(catalog.TryGetTemplate("P", out var part));
        Assert.True(index.TryFind(Assert.Single(part.RenderModels).MeshId!, out var mesh));
        Assert.Equal(new PreviewMeshRef(new PreviewAssetRef("Core", "Meshes/A.glb"), 1, "ABC"), mesh);
        Assert.Equal(new PreviewMeshRef(new PreviewAssetRef("Core", "Meshes/B.glb"), 1, "OnlyInB"), Find(index, "onlyinb"));
        Assert.Equal(new PreviewMeshRef(new PreviewAssetRef("Core", "Meshes/box.glb"), 0, "Box"), Find(index, "Box"));
        Assert.False(index.TryFind("HostOnlyMesh", out _));
        Assert.True(index.DuplicateNames.SetEquals(["ABC"]));
    }

    [Fact]
    public void ATruncatedGlb_GivesANotice_TheNextAtlasStillLoads_AndNoBinByteIsRead()
    {
        const string assets = """
            <MeshAtlas Path="Cut.glb" />
            <MeshAtlas Path="Good.glb" />
            """;
        var good = Glb(new byte[1024 * 1024], "Good");
        _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(assets)));
        _mods.Write("Core", "Cut.glb", good[..30]);
        _mods.Write("Core", "Good.glb", good);
        var counting = new CountingFiles(new FolderPreviewFiles(_mods.Folder("Core")));
        var core = _mods.Add("Core", counting);

        var catalog = PartCatalog.Build([core], CancellationToken.None);
        var index = MeshIndex.Build([core], catalog.MeshFiles, CancellationToken.None);

        Assert.Contains(index.Notices, notice => notice is { Kind: PreviewNoticeKind.AssetFailed, Subject: "Core/Cut.glb" });
        Assert.True(index.TryFind("good", out _));
        var binOffset = GlbContainer.Read(new MemoryStream(good)).BinOffset;
        Assert.InRange(counting.HeadBytes["Good.glb"], 1, binOffset - 8);
        Assert.DoesNotContain("Good.glb", counting.Opened);
    }

    private static PreviewMeshRef Find(MeshIndex index, string id)
    {
        Assert.True(index.TryFind(id, out var mesh));
        return mesh;
    }

    private static byte[] Glb(params string[] names) => Glb([], names);

    private static byte[] Glb(byte[] bin, params string[] names)
    {
        var meshes = string.Join(",", names.Select(name => $"{{\"name\":\"{name}\"}}"));
        return PreviewFilesTests.Glb($"{{\"asset\":{{\"version\":\"2.0\"}},\"meshes\":[{meshes}]}}", bin);
    }

    /// <summary>Counts the bytes read through head streams and records which files were opened whole.</summary>
    private sealed class CountingFiles(IPreviewFiles inner) : IPreviewFiles
    {
        public Dictionary<string, long> HeadBytes { get; } = [];

        public List<string> Opened { get; } = [];

        public bool Exists(string path) => inner.Exists(path);

        public Stream? TryOpenHead(string path)
        {
            if (inner.TryOpenHead(path) is not { } stream)
                return null;

            HeadBytes.TryAdd(path, 0);
            return new BoundedCountingStream(stream, long.MaxValue, count => HeadBytes[path] += count);
        }

        public Stream? TryOpen(string path)
        {
            Opened.Add(path);
            return inner.TryOpen(path);
        }

        public string? Describe(string path) => inner.Describe(path);

        public void Dispose() => inner.Dispose();
    }
}
