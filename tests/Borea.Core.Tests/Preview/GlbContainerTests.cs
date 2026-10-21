using System.Buffers.Binary;
using System.Text;
using Borea.Core.Preview;

namespace Borea.Core.Tests.Preview;

public sealed class GlbContainerTests
{
    private const string TwoMeshes = """{"asset":{"version":"2.0"},"meshes":[{"name":"Tank_A"},{"name":"Nozzle_B"}]}""";

    [Fact]
    public void Read_TwoNamedMeshes_ListsBothNamesInFileOrderAndFindsTheBinChunk()
    {
        var glb = Glb(TwoMeshes, bin: new byte[24]);
        using var stream = new MemoryStream(glb);

        var container = GlbContainer.Read(stream).WithCheckedBin(stream);

        Assert.Equal(["Tank_A", "Nozzle_B"], container.MeshNames);
        Assert.Equal(24, container.BinLength);
        Assert.Equal(glb.Length - 24, container.BinOffset);
    }

    [Fact]
    public void Read_ForwardOnlyStreamThatThrowsPastTheJsonChunk_StillListsTheNames()
    {
        var glb = Glb(TwoMeshes, bin: new byte[1024]);
        var jsonEnd = 20 + BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var stream = new ForwardOnlyStream(glb, jsonEnd);

        var container = GlbContainer.Read(stream);

        Assert.Equal(["Tank_A", "Nozzle_B"], container.MeshNames);
    }

    [Fact]
    public void Read_WrongMagic_ThrowsGlbFormatException()
    {
        var glb = Glb(TwoMeshes, bin: []);
        glb[0] = (byte)'X';

        Assert.Throws<GlbFormatException>(() => GlbContainer.Read(new MemoryStream(glb)));
    }

    [Fact]
    public void Read_TruncatedJsonChunk_ThrowsGlbFormatException()
    {
        var glb = Glb(TwoMeshes, bin: []);

        Assert.Throws<GlbFormatException>(() => GlbContainer.Read(new MemoryStream(glb[..30])));
    }

    private static byte[] Glb(string json, byte[] bin)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonPadded = (jsonBytes.Length + 3) & ~3;
        var binPadded = (bin.Length + 3) & ~3;
        var total = 12 + 8 + jsonPadded + (bin.Length > 0 ? 8 + binPadded : 0);
        var glb = new byte[total];
        var span = glb.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x4E4F534A);
        span.Slice(20, jsonPadded).Fill((byte)' ');
        jsonBytes.CopyTo(span[20..]);
        if (bin.Length > 0)
        {
            var binHeader = 20 + jsonPadded;
            BinaryPrimitives.WriteUInt32LittleEndian(span[binHeader..], (uint)binPadded);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(binHeader + 4)..], 0x004E4942);
            bin.CopyTo(span[(binHeader + 8)..]);
        }

        return glb;
    }

    /// <summary>A stream that cannot seek and fails the test when a read reaches past <c>limit</c>.</summary>
    private sealed class ForwardOnlyStream(byte[] data, int limit) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length > 0 && _position + buffer.Length > limit)
                throw new InvalidOperationException("The reader asked for bytes past the JSON chunk.");

            var count = Math.Min(buffer.Length, data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
