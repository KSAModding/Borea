using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace Borea.Preview.Tests.Gltf;

/// <summary>
/// Writes small GLB files for the tests: the 12-byte header, the JSON chunk padded with spaces and the BIN chunk
/// padded with zeros, as the GLB specification lays them out.
/// </summary>
internal static class GlbTestWriter
{
    public const int FloatComponent = 5126;
    public const int UnsignedByteComponent = 5121;
    public const int UnsignedShortComponent = 5123;
    public const int UnsignedIntComponent = 5125;

    public static byte[] Pack(JsonNode json, byte[] bin)
    {
        var jsonBytes = Pad(Encoding.UTF8.GetBytes(json.ToJsonString()), (byte)' ');
        var binBytes = Pad(bin, 0);
        var file = new byte[12 + 8 + jsonBytes.Length + 8 + binBytes.Length];
        var span = file.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x4E4F534A);
        jsonBytes.CopyTo(span[20..]);
        var binHeader = span[(20 + jsonBytes.Length)..];
        BinaryPrimitives.WriteUInt32LittleEndian(binHeader, (uint)binBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(binHeader[4..], 0x004E4942);
        binBytes.CopyTo(binHeader[8..]);
        return file;
    }

    public static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    /// <summary>Writes each index with the size of the glTF component type.</summary>
    public static byte[] Indices(int componentType, params uint[] values)
    {
        var size = componentType switch
        {
            UnsignedByteComponent => 1,
            UnsignedShortComponent => 2,
            _ => 4,
        };

        var bytes = new byte[values.Length * size];
        for (var i = 0; i < values.Length; i++)
        {
            var span = bytes.AsSpan(i * size);
            switch (size)
            {
                case 1:
                    span[0] = (byte)values[i];
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)values[i]);
                    break;
                default:
                    BinaryPrimitives.WriteUInt32LittleEndian(span, values[i]);
                    break;
            }
        }

        return bytes;
    }

    private static byte[] Pad(byte[] data, byte fill)
    {
        var padded = new byte[(data.Length + 3) & ~3];
        data.CopyTo(padded, 0);
        padded.AsSpan(data.Length).Fill(fill);
        return padded;
    }
}
