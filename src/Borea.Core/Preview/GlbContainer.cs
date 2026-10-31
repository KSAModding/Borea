using System.Buffers.Binary;
using System.Text.Json;

namespace Borea.Core.Preview;

/// <summary>
/// The header and JSON chunk of a binary glTF (GLB) file, read in one forward pass that stops after the JSON chunk.
/// The catalog uses it to list the mesh names, which are the Ids the game registers meshes under
/// (MeshAtlasFileReference.DoLoad), and the vertex decoder uses it to find the JSON and the BIN chunk.
/// </summary>
public sealed class GlbContainer
{
    /// <summary>The largest JSON chunk the reader accepts, so an author file cannot make Borea allocate without limit.</summary>
    public const int MaxJsonLength = 16 * 1024 * 1024;

    private const uint Magic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;
    private const int HeaderLength = 12;
    private const int ChunkHeaderLength = 8;

    private GlbContainer(ReadOnlyMemory<byte> json, IReadOnlyList<string?> meshNames, long declaredLength, long binOffset, long? binLength)
    {
        Json = json;
        MeshNames = meshNames;
        DeclaredLength = declaredLength;
        BinOffset = binOffset;
        BinLength = binLength;
    }

    /// <summary>The UTF-8 bytes of the JSON chunk.</summary>
    public ReadOnlyMemory<byte> Json { get; }

    /// <summary>The name of each glTF mesh in file order, so the index of a name is its mesh index. A mesh without a string name gives null.</summary>
    public IReadOnlyList<string?> MeshNames { get; }

    /// <summary>The total length that the GLB header declares.</summary>
    public long DeclaredLength { get; }

    /// <summary>The offset of the first byte of BIN chunk data from the start of the file.</summary>
    public long BinOffset { get; }

    /// <summary>
    /// The exact length of the BIN chunk data, or null until <see cref="WithCheckedBin"/> reads the BIN chunk header.
    /// <see cref="Read"/> always gives null, because its forward pass stops before that header. A file without a BIN
    /// chunk gives 0 after <see cref="WithCheckedBin"/>.
    /// </summary>
    public long? BinLength { get; }

    /// <summary>
    /// Reads the 12-byte header and the JSON chunk and stops there, so it works on a stream that cannot seek and reads
    /// no BIN chunk byte. The stream is left after the JSON chunk.
    /// </summary>
    /// <exception cref="GlbFormatException">The magic, the version or the first chunk is wrong, a chunk is cut short, or the JSON is not valid.</exception>
    public static GlbContainer Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> header = stackalloc byte[HeaderLength + ChunkHeaderLength];
        ReadExactly(stream, header, "header");
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic)
            throw new GlbFormatException("The file does not start with the GLB magic.");

        var version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (version != 2)
            throw new GlbFormatException($"The GLB version is {version}, and only version 2 is supported.");

        var declaredLength = (long)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header[16..]) != JsonChunkType)
            throw new GlbFormatException("The first GLB chunk is not the JSON chunk.");

        if (jsonLength > MaxJsonLength)
            throw new GlbFormatException($"The JSON chunk has {jsonLength} bytes, more than the limit of {MaxJsonLength}.");

        var jsonEnd = HeaderLength + ChunkHeaderLength + (long)jsonLength;
        if (declaredLength < jsonEnd)
            throw new GlbFormatException("The GLB header declares a total length that ends inside the JSON chunk.");

        var json = new byte[jsonLength];
        ReadExactly(stream, json, "JSON chunk");

        return new GlbContainer(json, ReadMeshNames(json), declaredLength, jsonEnd + ChunkHeaderLength, binLength: null);
    }

    /// <summary>
    /// Reads the BIN chunk header from a stream that can seek and gives a copy whose <see cref="BinLength"/> is set.
    /// A file without a BIN chunk gives a length of 0. The stream position is left after the chunk header.
    /// </summary>
    /// <exception cref="GlbFormatException">The second chunk is not a BIN chunk, or it extends past the declared length or the end of the stream.</exception>
    public GlbContainer WithCheckedBin(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
            throw new ArgumentException("The stream must be able to seek.", nameof(stream));

        var headerOffset = BinOffset - ChunkHeaderLength;
        var end = Math.Min(DeclaredLength, stream.Length);
        if (end - headerOffset < ChunkHeaderLength)
            return new GlbContainer(Json, MeshNames, DeclaredLength, BinOffset, 0);

        stream.Position = headerOffset;
        Span<byte> chunkHeader = stackalloc byte[ChunkHeaderLength];
        ReadExactly(stream, chunkHeader, "BIN chunk header");
        var length = (long)BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader);
        if (BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]) != BinChunkType)
            throw new GlbFormatException("The second GLB chunk is not the BIN chunk.");

        if (length > end - BinOffset)
            throw new GlbFormatException("The BIN chunk extends past the end of the file.");

        return new GlbContainer(Json, MeshNames, DeclaredLength, BinOffset, length);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer, string part)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new GlbFormatException($"The GLB file ends inside its {part}.", exception);
        }
    }

    private static string?[] ReadMeshNames(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new GlbFormatException("The GLB JSON chunk is not a JSON object.");

            if (!document.RootElement.TryGetProperty("meshes", out var meshes) || meshes.ValueKind != JsonValueKind.Array)
                return [];

            var names = new string?[meshes.GetArrayLength()];
            var index = 0;
            foreach (var mesh in meshes.EnumerateArray())
            {
                names[index++] = mesh.ValueKind == JsonValueKind.Object && mesh.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString()
                    : null;
            }

            return names;
        }
        catch (JsonException exception)
        {
            throw new GlbFormatException("The GLB JSON chunk is not valid JSON.", exception);
        }
    }
}
