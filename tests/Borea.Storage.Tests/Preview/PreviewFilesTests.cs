using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Borea.Core.Preview;
using Borea.Storage.Preview.Sources;

namespace Borea.Storage.Tests.Preview;

public sealed class PreviewFilesTests : IDisposable
{
    private const long OneMiB = 1024 * 1024;
    private static readonly PreviewFileCaps SmallCaps = new(OneMiB, OneMiB);

    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid())).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void AFile_ReadsTheSameThroughTheFolderAndTheZip_AndNothingIsWritten()
    {
        var bytes = Encoding.UTF8.GetBytes("<Part Id=\"A\"/>");
        var folder = Directory.CreateDirectory(Path.Combine(_root, "mod", "Parts")).Parent!.FullName;
        File.WriteAllBytes(Path.Combine(folder, "Parts", "A.xml"), bytes);
        var zip = WriteZip("mod.zip", ("Parts/A.xml", bytes));
        var before = Snapshot();

        var cache = new PreviewEntryCache();
        using (var set = new PreviewAssetSet(cache, [
            new("Folder", new FolderPreviewFiles(folder)),
            new("Zip", new ZipPreviewFiles(zip, null, null, null, cache)),
        ]))
        {
            Assert.Equal(bytes, ReadAll(set.TryOpenRead(new PreviewAssetRef("Folder", "Parts/A.xml"))));
            Assert.Equal(bytes, ReadAll(set.TryOpenRead(new PreviewAssetRef("zip", "Parts/A.xml"))));
            Assert.Equal(bytes, ReadAll(set.TryOpenHead(new PreviewAssetRef("Zip", "./Parts/A.xml"))));
            Assert.Null(set.TryOpenRead(new PreviewAssetRef("Other", "Parts/A.xml")));
        }

        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("Parts/../../x")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/x")]
    [InlineData("\\\\server\\share\\x")]
    public void TraversalAndRootedPaths_AreRefusedByBothSources(string path)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "mod", "inner")).FullName;
        File.WriteAllText(Path.Combine(_root, "mod", "x"), "outside");
        var zip = WriteZip("mod.zip", ("x", [1]));

        Assert.Null(PreviewPath.Normalize(path));
        using var files = new FolderPreviewFiles(folder);
        using var zipped = new ZipPreviewFiles(zip, null, null, null, new PreviewEntryCache());
        Assert.Null(files.TryOpen(path));
        Assert.Null(zipped.TryOpen(path));
    }

    [Fact]
    public void AZipEntry_IsFoundInAnyCaseAndWithBackslashes_AndTraversalEntriesAreLeftOut()
    {
        var zip = WriteZip("mod.zip", ("Meshes/A.glb", [7]), ("../evil.glb", [9]));

        using var files = new ZipPreviewFiles(zip, null, null, null, new PreviewEntryCache());

        Assert.Equal([7], ReadAll(files.TryOpen("meshes\\a.glb")));
        Assert.False(files.Exists("evil.glb"));
    }

    [Fact]
    public void ANestedZipRoot_MapsPathsRelativeToTheModFolder()
    {
        var zip = WriteZip("mod.zip", ("MyMod/mod.toml", [1]), ("MyMod/Meshes/A.glb", [2]));

        using var files = ZipPreviewFiles.Open(zip, null, ["mod.toml"], null, null, new PreviewEntryCache());

        Assert.Equal([2], ReadAll(files.TryOpen("Meshes/A.glb")));
        Assert.False(files.Exists("MyMod/Meshes/A.glb"));
    }

    [Fact]
    public void AnEntryLongerThanItsCap_GivesNull_AfterAtMostTheCapOfRealBytes()
    {
        var big = new byte[3 * OneMiB];
        var honest = WriteZip("honest.zip", ("Meshes/big.glb", big));
        var lying = WriteZip("lying.zip", ("Meshes/big.glb", big));
        DeclareUncompressedLength(lying, 100);
        using (var archive = ZipFile.OpenRead(lying))
        {
            Assert.Equal(100, archive.GetEntry("Meshes/big.glb")!.Length);
        }

        using var honestFiles = new ZipPreviewFiles(honest, null, null, null, new PreviewEntryCache(), SmallCaps);
        Assert.Null(honestFiles.TryOpen("Meshes/big.glb"));
        Assert.InRange(honestFiles.DecompressedBytes, OneMiB, OneMiB + 1);

        // The runtime stops the inflater at the declared length, so a small claim can only cut the entry short. The
        // count shows that the source never goes past its own cap whatever the claim.
        using var lyingFiles = new ZipPreviewFiles(lying, null, null, null, new PreviewEntryCache(), SmallCaps);
        var stream = lyingFiles.TryOpen("Meshes/big.glb");
        Assert.True(stream is null || stream.Length <= OneMiB);
        Assert.InRange(lyingFiles.DecompressedBytes, 0, OneMiB + 1);
    }

    [Fact]
    public void TheHeadOfAZippedGlb_DecompressesOnlyTheHeaderAndJson_AndTheFullReadIsCachedOnce()
    {
        const string json = "{\"asset\":{\"version\":\"2.0\"},\"meshes\":[{\"name\":\"Tank\"}]}";
        var bin = new byte[2 * OneMiB];
        new Random(5).NextBytes(bin);
        var zip = WriteZip("mod.zip", ("Meshes/Tank.glb", Glb(json, bin)));
        var cache = new PreviewEntryCache();
        using var files = new ZipPreviewFiles(zip, null, null, null, cache);

        GlbContainer glb;
        using (var head = files.TryOpenHead("Meshes/Tank.glb")!)
            glb = GlbContainer.Read(head);

        Assert.Equal(["Tank"], glb.MeshNames);
        Assert.InRange(files.DecompressedBytes, 1, glb.BinOffset + (64 * 1024));

        var afterHead = files.DecompressedBytes;
        using (var first = files.TryOpen("Meshes/Tank.glb")!)
            Assert.True(first.CanSeek);

        var afterFirst = files.DecompressedBytes;
        Assert.True(afterFirst - afterHead >= bin.Length);
        using (var second = files.TryOpen("MESHES/tank.glb")!)
            Assert.Equal(glb.DeclaredLength, second.Length);

        Assert.Equal(afterFirst, files.DecompressedBytes);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void AfterDispose_TheZipAndItsOpenHeadStreamThrowObjectDisposed()
    {
        var zip = WriteZip("mod.zip", ("Meshes/A.glb", [1, 2, 3]));
        var files = new ZipPreviewFiles(zip, null, null, null, new PreviewEntryCache());
        using var pending = files.TryOpenHead("Meshes/A.glb")!;

        files.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pending.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => files.TryOpen("Meshes/A.glb"));
    }

    [Fact]
    public async Task Dispose_DuringALargeRead_StopsTheReadAtItsNextChunk_AndDoesNotWaitForTheWholeEntry()
    {
        var size = 64 * OneMiB;
        var zip = WriteZip("big.zip", ("Meshes/Big.glb", new byte[size]));
        var files = new ZipPreviewFiles(zip, null, null, null, new PreviewEntryCache());
        var chunks = 0;
        files.BeforeChunk = () =>
        {
            // Dispose between the first and the second chunk. The archive lock is reentrant, so this thread may take it.
            if (Interlocked.Increment(ref chunks) == 2)
                files.Dispose();
        };

        var read = Task.Run(() => files.TryOpen("Meshes/Big.glb"));

        await Assert.ThrowsAsync<ObjectDisposedException>(() => read);
        Assert.InRange(files.DecompressedBytes, 1, size - 1);
    }

    [Fact]
    public void TheEntryCache_EvictsTheLeastRecentlyUsedEntryAboveItsCapacity()
    {
        var cache = new PreviewEntryCache(capacityBytes: 10);
        cache.Add("a", new byte[4]);
        cache.Add("b", new byte[4]);
        Assert.True(cache.TryGet("a", out _));

        cache.Add("c", new byte[4]);
        cache.Add("huge", new byte[11]);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.False(cache.TryGet("huge", out _));
        Assert.Equal(8, cache.TotalBytes);
    }

    [Fact]
    public void AFolderFile_DeletedAfterTheSourceOpened_GivesNull_AndItsDescriptionFollowsLengthAndTime()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "mod")).FullName;
        var file = Path.Combine(folder, "A.glb");
        File.WriteAllBytes(file, [1, 2, 3]);
        using var files = new FolderPreviewFiles(folder);

        var first = files.Describe("A.glb");
        File.WriteAllBytes(file, [1, 2, 3, 4]);
        File.SetLastWriteTimeUtc(file, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var longer = files.Describe("a.GLB");
        File.SetLastWriteTimeUtc(file, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var touched = files.Describe("A.glb");

        Assert.NotNull(first);
        Assert.NotEqual(first, longer);
        Assert.NotEqual(longer, touched);

        File.Delete(file);
        Assert.Null(files.TryOpen("A.glb"));
        Assert.Null(files.TryOpenHead("A.glb"));
        Assert.Null(files.Describe("A.glb"));
    }

    [Fact]
    public void GameCoreFiles_ListsTheContentModsInManifestOrder_AndReportsAMissingOrInvalidGame()
    {
        var game = Path.Combine(_root, "game");
        Assert.Equal(GameCoreStatus.NoGame, GameCoreFiles.Open(game).Status);
        Assert.Equal(GameCoreStatus.NoGame, GameCoreFiles.Open(null).Status);
        Directory.CreateDirectory(Path.Combine(game, "Content"));
        Assert.Equal(GameCoreStatus.GameInvalid, GameCoreFiles.Open(game).Status);

        foreach (var id in new[] { "Core", "Extra", "Off" })
        {
            Directory.CreateDirectory(Path.Combine(game, "Content", id));
            File.WriteAllText(Path.Combine(game, "Content", id, "mod.toml"), $"name = \"{id}\"");
        }

        File.WriteAllText(
            Path.Combine(game, "Content", "manifest.toml"),
            "[[mods]]\nid = \"Core\"\nenabled = true\n\n[[mods]]\nid = \"Extra\"\nenabled = true\n\n[[mods]]\nid = \"Off\"\nenabled = false\n\n[[mods]]\nid = \"Gone\"\nenabled = true\n");

        var core = GameCoreFiles.Open(game);

        Assert.Equal(GameCoreStatus.Ok, core.Status);
        Assert.Equal(["Core", "Extra"], core.Mods.Select(mod => mod.Key));
        Assert.True(core.Mods[0].Value.Exists("mod.toml"));
    }

    private string WriteZip(string name, params (string Name, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(_root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, bytes) in entries)
        {
            using var stream = archive.CreateEntry(entryName).Open();
            stream.Write(bytes);
        }

        return path;
    }

    /// <summary>Writes a false uncompressed size into the local header and the central directory of the only entry.</summary>
    private static void DeclareUncompressedLength(string zipPath, uint length)
    {
        var bytes = File.ReadAllBytes(zipPath);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(22), length);
        for (var index = bytes.Length - 22; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index)) == 0x02014b50)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index + 24), length);
                break;
            }
        }

        File.WriteAllBytes(zipPath, bytes);
    }

    private string[] Snapshot() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Select(path => $"{path}|{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path).Ticks}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static byte[] ReadAll(Stream? stream)
    {
        Assert.NotNull(stream);
        using (stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    private static byte[] Glb(string json, byte[] bin)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonPadded = (jsonBytes.Length + 3) & ~3;
        var total = 12 + 8 + jsonPadded + 8 + bin.Length;
        var glb = new byte[total];
        var span = glb.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x4E4F534A);
        span.Slice(20, jsonPadded).Fill((byte)' ');
        jsonBytes.CopyTo(span[20..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(20 + jsonPadded)..], (uint)bin.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(24 + jsonPadded)..], 0x004E4942);
        bin.CopyTo(span[(28 + jsonPadded)..]);
        return glb;
    }
}
