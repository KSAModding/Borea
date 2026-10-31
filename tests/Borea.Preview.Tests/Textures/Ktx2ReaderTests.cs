using Borea.Preview.Textures;

namespace Borea.Preview.Tests.Textures;

public sealed class Ktx2ReaderTests
{
    private const uint Rgba8 = 37;

    private static readonly byte[] Level0 = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
    private static readonly byte[] Level1 = Enumerable.Range(100, 16).Select(value => (byte)value).ToArray();

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    public void ATargetOfTwo_ReadsOnlyLevelOne(uint scheme)
    {
        var file = Ktx2TestWriter.Write(Rgba8, 4, 4, [Level0, Level1], scheme);
        using var stream = new GuardedStream(file);

        var header = Ktx2Reader.ReadHeader(stream);
        Assert.Equal(Ktx2Status.Ok, header.Status);
        var level0 = header.Value!.Levels[0];
        stream.Forbid(level0.ByteOffset, level0.ByteLength);

        var chosen = Ktx2Reader.ChooseLevel(header.Value, 2);
        var level = Ktx2Reader.ReadLevel(stream, header.Value, chosen);

        Assert.Equal(1, chosen);
        Assert.Equal(Ktx2Status.Ok, level.Status);
        Assert.Equal((2, 2), (level.Value!.Width, level.Value.Height));
        Assert.Equal(Level1, level.Value.Data);
    }

    public static TheoryData<string, uint, Action<byte[]>> BrokenFiles => new()
    {
        { "wrong identifier", 0u, file => file[1] = (byte)'X' },
        { "level past the end", 0u, file => Ktx2TestWriter.SetLevelField(file, 1, 0, (ulong)file.Length - 8) },
        { "huge level offset", 0u, file => Ktx2TestWriter.SetLevelField(file, 1, 0, long.MaxValue - 4) },
        { "declared size does not fit the format", 0u, file => Ktx2TestWriter.SetLevelField(file, 1, 2, 15) },
        { "stored size does not fit the format", 0u, file => Ktx2TestWriter.SetLevelField(file, 1, 1, 15) },
        { "zstd data that is not zstd", 2u, file => file[Ktx2TestWriter.LevelIndexOffset + 48] ^= 0xFF },
    };

    [Theory]
    [MemberData(nameof(BrokenFiles))]
    public void ABrokenFile_IsInvalid(string name, uint scheme, Action<byte[]> breakFile)
    {
        var file = Ktx2TestWriter.Write(Rgba8, 4, 4, [Level0, Level1], scheme);
        breakFile(file);

        var status = Read(file, 1);
        Assert.True(status == Ktx2Status.Invalid, $"{name} gave {status}.");
    }

    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    public void ZstdDataThatInflatesToAnotherSize_IsInvalid(int inflatedLength)
    {
        var file = Ktx2TestWriter.Write(Rgba8, 2, 2, [new byte[16]], scheme: 2);
        var other = Ktx2TestWriter.Write(Rgba8, 2, 2, [new byte[inflatedLength]], scheme: 2);

        // Takes the stored frame of the other file, keeps the declared 16 bytes.
        var frame = other[(Ktx2TestWriter.LevelIndexOffset + 24)..];
        var broken = file[..(Ktx2TestWriter.LevelIndexOffset + 24)].Concat(frame).ToArray();
        Ktx2TestWriter.SetLevelField(broken, 0, 1, (ulong)frame.Length);

        Assert.Equal(Ktx2Status.Invalid, Read(broken, 0));
    }

    [Fact]
    public void AFileShorterThanItsHeaderOrIndex_IsInvalid()
    {
        var file = Ktx2TestWriter.Write(Rgba8, 4, 4, [Level0, Level1]);

        Assert.Equal(Ktx2Status.Invalid, Ktx2Reader.ReadHeader(new MemoryStream(file[..40])).Status);
        Assert.Equal(Ktx2Status.Invalid, Ktx2Reader.ReadHeader(new MemoryStream(file[..100])).Status);
    }

    [Theory]
    [InlineData(145u, 1u, 0u, 1u)]
    [InlineData(0u, 0u, 0u, 1u)]
    [InlineData(Rgba8, 0u, 2u, 1u)]
    [InlineData(Rgba8, 0u, 0u, 6u)]
    [InlineData(140u, 0u, 0u, 1u)]
    public void BasisArraysCubemapsAndUnknownFormats_AreUnsupported(uint vkFormat, uint scheme, uint layers, uint faces)
    {
        var file = Ktx2TestWriter.Write(vkFormat, 4, 4, [Level0, Level1], 0, layers, faces);
        BitConverter.TryWriteBytes(file.AsSpan(44), scheme);

        Assert.Equal(Ktx2Status.Unsupported, Ktx2Reader.ReadHeader(new MemoryStream(file)).Status);
    }

    [GameDirFact]
    public void TheCoreFuelTankDiffuse_HasTheExpectedHeader_AndItsSmallLevelInflates()
    {
        var path = Path.Combine(GameDirFactAttribute.GameDir!, "Content", "Core", "Textures", "CoreFuelTankA_TextureAtlas_Diffuse.ktx2");
        using var stream = File.OpenRead(path);

        var header = Ktx2Reader.ReadHeader(stream);
        Assert.Equal(Ktx2Status.Ok, header.Status);
        Assert.Equal(145, header.Value!.VkFormat);
        Assert.Equal((2048, 2048), (header.Value.Width, header.Value.Height));
        Assert.Equal(12, header.Value.Levels.Count);
        Assert.Equal(2, header.Value.SupercompressionScheme);

        var level = Ktx2Reader.ReadLevel(stream, header.Value, Ktx2Reader.ChooseLevel(header.Value, 512));
        Assert.Equal(Ktx2Status.Ok, level.Status);
        Assert.Equal((2, 512 * 512), (level.Value!.Level, level.Value.Data.Length));
    }

    /// <summary>The status of the header read when it fails, else the status of the level read.</summary>
    private static Ktx2Status Read(byte[] file, int level)
    {
        using var stream = new MemoryStream(file);
        var header = Ktx2Reader.ReadHeader(stream);
        return header.Status == Ktx2Status.Ok ? Ktx2Reader.ReadLevel(stream, header.Value!, level).Status : header.Status;
    }

    /// <summary>A seekable stream that throws when a read touches a forbidden byte range.</summary>
    private sealed class GuardedStream(byte[] data) : MemoryStream(data, writable: false)
    {
        private long _start = -1;
        private long _end = -1;

        public void Forbid(long offset, long length) => (_start, _end) = (offset, offset + length);

        public override int Read(byte[] buffer, int offset, int count)
        {
            Check(count);
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Check(buffer.Length);
            return base.Read(buffer);
        }

        private void Check(int count)
        {
            if (Position < _end && Position + count > _start)
            {
                throw new InvalidOperationException($"The read at {Position} touches the forbidden range.");
            }
        }
    }
}
