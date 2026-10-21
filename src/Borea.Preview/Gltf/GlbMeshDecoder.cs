using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Borea.Core.Preview;
using Borea.Preview.Scene;

namespace Borea.Preview.Gltf;

/// <summary>
/// Decodes the triangles of one glTF mesh of a GLB file into a <see cref="PreviewMesh"/>. It reads only the
/// accessor ranges of that mesh from the BIN chunk, so a mesh atlas with hundreds of meshes costs only the bytes
/// of the one the vehicle uses.
/// </summary>
/// <remarks>
/// The game registers each glTF mesh of an atlas by index (MeshAtlasFileReference.DoLoad) and loads its primitives
/// with GltfUtils.LoadMeshAssetForPrim in MeshReference.Load, but the part renderers draw only
/// MeshReference.DeviceMeshInterleaved, which is primitive 0 (PartModel and GenericMeshRenderer.AddInstance). The
/// decoder therefore reads primitive 0 only. It also ignores the node hierarchy, because MeshReference.Load reads
/// the mesh by index and never looks at a node. Only float POSITION, NORMAL and TEXCOORD_0 are read, which is
/// what every Core mesh atlas of build 2026.10.19.5638 stores. The UV origin is the top-left corner, as glTF
/// defines it.
/// </remarks>
public static class GlbMeshDecoder
{
    private const int TrianglesMode = 4;
    private const int FloatComponent = 5126;
    private const int UnsignedByteComponent = 5121;
    private const int UnsignedShortComponent = 5123;
    private const int UnsignedIntComponent = 5125;

    /// <summary>The largest byteStride that glTF allows for a vertex attribute.</summary>
    private const int MaxStride = 252;

    /// <summary>
    /// The triangle limit that <see cref="Decode"/> uses when the caller gives none. It is the larger triangle budget
    /// of the whole preview, so one mesh can never need more than the whole scene may hold.
    /// </summary>
    public const int DefaultMaxTriangles = 2_000_000;

    /// <summary>
    /// Gives the vertex and triangle counts of primitive 0 of the mesh at <paramref name="meshIndex"/> from the JSON
    /// chunk only. It reads no stream, so the scene builder can apply its triangle budget before it decodes a mesh.
    /// </summary>
    /// <exception cref="GlbFormatException">The mesh does not exist, uses a required extension or a mode other than triangles, or an accessor count is missing.</exception>
    public static GlbMeshSize Measure(GlbContainer container, int meshIndex)
    {
        ArgumentNullException.ThrowIfNull(container);
        return WithJson(container, root => MeasurePrimitive(root, meshIndex, out _));
    }

    /// <summary>Decodes primitive 0 of the mesh at <paramref name="meshIndex"/>.</summary>
    /// <param name="stream">A seekable stream over the whole GLB file. Its position changes.</param>
    /// <param name="container">The container read from the same file. Its BIN chunk is checked here when it was not checked before.</param>
    /// <param name="maxTriangles">
    /// The most triangles the mesh may have. The mesh may also have at most three vertices per allowed triangle. Both
    /// counts are checked from the JSON before any array is allocated or any accessor byte is read.
    /// </param>
    /// <exception cref="GlbFormatException">
    /// The mesh does not exist, is larger than <paramref name="maxTriangles"/> allows, uses an external buffer, a
    /// sparse accessor, a required extension, a mode other than triangles or a component type the preview does not
    /// read, or one of its counts, offsets, strides or indices does not fit the data.
    /// </exception>
    public static PreviewMesh Decode(Stream stream, GlbContainer container, int meshIndex, int maxTriangles = DefaultMaxTriangles)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(container);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTriangles);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The stream must be able to seek.", nameof(stream));
        }

        if (container.BinLength is null)
        {
            container = container.WithCheckedBin(stream);
        }

        return WithJson(container, root => Decode(stream, root, container.BinOffset, container.BinLength!.Value, meshIndex, maxTriangles));
    }

    private static T WithJson<T>(GlbContainer container, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(container.Json);
            return read(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new GlbFormatException("The GLB JSON chunk is not valid JSON.", exception);
        }
        catch (Exception exception) when (exception is KeyNotFoundException || (exception is InvalidOperationException && exception is not ObjectDisposedException))
        {
            // JsonElement throws these when a property is missing or has another JSON kind than the code asks for.
            // ObjectDisposedException is an InvalidOperationException too, but it means cancellation and must pass.
            throw new GlbFormatException("A glTF property is missing or has the wrong JSON type.", exception);
        }
    }

    private static PreviewMesh Decode(Stream stream, JsonElement root, long binOffset, long binLength, int meshIndex, int maxTriangles)
    {
        // The arrays below grow with these counts, and with u8 indices or the flat-normal pass they grow much faster
        // than the file. The counts are therefore limited before anything is allocated, so a large or hostile mesh
        // fails as one asset instead of with an OutOfMemoryException. A byte range that is read is bounded by the
        // checked BIN chunk, so it never needs more memory than the file has.
        var size = MeasurePrimitive(root, meshIndex, out var primitive);
        if (size.TriangleCount > maxTriangles || size.VertexCount > 3L * maxTriangles)
        {
            throw new GlbFormatException($"Mesh {meshIndex} has {size.TriangleCount} triangles and {size.VertexCount} vertices, more than the limit of {maxTriangles} triangles allows.");
        }

        var reader = new AccessorReader(stream, root, binOffset, binLength);
        var attributes = primitive.GetProperty("attributes");
        var positions = reader.ReadFloats(ToIndex(attributes.GetProperty("POSITION")), "VEC3", out var vertexCount);
        float[]? normals = null;
        if (attributes.TryGetProperty("NORMAL", out var normalIndex))
        {
            normals = reader.ReadFloats(ToIndex(normalIndex), "VEC3", out var normalCount);
            RequireCount(normalCount, vertexCount, "NORMAL");
        }

        float[]? uvs = null;
        if (attributes.TryGetProperty("TEXCOORD_0", out var uvIndex))
        {
            uvs = reader.ReadFloats(ToIndex(uvIndex), "VEC2", out var uvCount);
            RequireCount(uvCount, vertexCount, "TEXCOORD_0");
        }

        uint[] indices;
        if (primitive.TryGetProperty("indices", out var indicesIndex))
        {
            indices = reader.ReadIndices(ToIndex(indicesIndex), vertexCount);
        }
        else
        {
            indices = new uint[vertexCount];
            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = (uint)i;
            }
        }

        if (indices.Length % 3 != 0)
        {
            throw new GlbFormatException($"Primitive 0 of mesh {meshIndex} has {indices.Length} indices, which is not a multiple of 3.");
        }

        if (normals is null)
        {
            (positions, normals, uvs, indices) = WithFlatNormals(positions, uvs, indices);
        }

        try
        {
            return new PreviewMesh(positions, normals, uvs, indices);
        }
        catch (ArgumentException exception)
        {
            throw new GlbFormatException($"Mesh {meshIndex} has a value that is not finite.", exception);
        }
    }

    /// <summary>
    /// Finds primitive 0 of the mesh and gives the counts that its POSITION and index accessors declare. The triangle
    /// count is the index count, or the vertex count of a non-indexed mesh, divided by 3.
    /// </summary>
    private static GlbMeshSize MeasurePrimitive(JsonElement root, int meshIndex, out JsonElement primitive)
    {
        if (root.TryGetProperty("extensionsRequired", out var required) && required.ValueKind == JsonValueKind.Array && required.GetArrayLength() > 0)
        {
            throw new GlbFormatException($"The file requires the glTF extension {required[0]}, which the preview does not read.");
        }

        var meshes = GetArray(root, "meshes");
        if (meshIndex < 0 || meshIndex >= meshes.GetArrayLength())
        {
            throw new GlbFormatException($"The file has no mesh {meshIndex}.");
        }

        var primitives = GetArray(meshes[meshIndex], "primitives");
        if (primitives.GetArrayLength() == 0)
        {
            throw new GlbFormatException($"Mesh {meshIndex} has no primitive.");
        }

        primitive = primitives[0];
        var mode = GetInt(primitive, "mode", TrianglesMode);
        if (mode != TrianglesMode)
        {
            throw new GlbFormatException($"Primitive 0 of mesh {meshIndex} uses mode {mode}, and the preview reads only triangles (mode 4).");
        }

        if (!primitive.GetProperty("attributes").TryGetProperty("POSITION", out var positionIndex))
        {
            throw new GlbFormatException($"Primitive 0 of mesh {meshIndex} has no POSITION attribute.");
        }

        var accessors = GetArray(root, "accessors");
        var positionAccessor = ToIndex(positionIndex);
        var vertexCount = GetCount(accessors, positionAccessor);
        var cornerCount = primitive.TryGetProperty("indices", out var indicesIndex) ? GetCount(accessors, ToIndex(indicesIndex)) : vertexCount;
        return new GlbMeshSize(vertexCount, cornerCount / 3)
        {
            Bounds = ReadDeclaredBounds(accessors[positionAccessor]),
            HasNormals = primitive.GetProperty("attributes").TryGetProperty("NORMAL", out _),
        };
    }

    private static PreviewBounds? ReadDeclaredBounds(JsonElement accessor)
    {
        if (accessor.ValueKind != JsonValueKind.Object
            || !TryReadVector(accessor, "min", out var min)
            || !TryReadVector(accessor, "max", out var max))
        {
            return null;
        }

        var bounds = new PreviewBounds(min, max);
        return bounds.IsFinite ? bounds : null;
    }

    private static bool TryReadVector(JsonElement accessor, string name, out Vector3 vector)
    {
        vector = default;
        if (!accessor.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 3)
        {
            return false;
        }

        Span<float> values = stackalloc float[3];
        for (var i = 0; i < 3; i++)
        {
            if (array[i].ValueKind != JsonValueKind.Number || !array[i].TryGetSingle(out values[i]))
            {
                return false;
            }
        }

        vector = new Vector3(values[0], values[1], values[2]);
        return true;
    }

    private static int GetCount(JsonElement accessors, int accessorIndex)
    {
        if (accessorIndex >= accessors.GetArrayLength())
        {
            throw new GlbFormatException($"The file has no accessor {accessorIndex}.");
        }

        return GetInt(accessors[accessorIndex], "count", 0);
    }

    /// <summary>
    /// Gives every triangle its own three vertices with the unit normal of its face, because a normal belongs to a
    /// vertex and an indexed vertex can sit on faces that point in different directions. A front face is
    /// counterclockwise, so the normal is (b - a) x (c - a). A degenerate face gets +Z so that every normal is a
    /// unit vector.
    /// </summary>
    private static (float[] Positions, float[] Normals, float[]? Uvs, uint[] Indices) WithFlatNormals(float[] positions, float[]? uvs, uint[] indices)
    {
        if ((long)indices.Length * 3 > Array.MaxLength)
        {
            throw new GlbFormatException("The mesh has too many triangles to compute flat normals.");
        }

        var flatPositions = new float[indices.Length * 3];
        var flatNormals = new float[indices.Length * 3];
        var flatUvs = uvs is null ? null : new float[indices.Length * 2];
        var flatIndices = new uint[indices.Length];
        for (var corner = 0; corner < indices.Length; corner += 3)
        {
            var a = Read3(positions, indices[corner]);
            var b = Read3(positions, indices[corner + 1]);
            var c = Read3(positions, indices[corner + 2]);
            var normal = Vector3.Cross(b - a, c - a);
            var length = normal.Length();
            normal = length > 0f && float.IsFinite(length) ? normal / length : Vector3.UnitZ;

            for (var k = 0; k < 3; k++)
            {
                var vertex = corner + k;
                var source = (int)indices[vertex];
                Array.Copy(positions, source * 3, flatPositions, vertex * 3, 3);
                normal.CopyTo(flatNormals, vertex * 3);
                if (flatUvs is not null)
                {
                    Array.Copy(uvs!, source * 2, flatUvs, vertex * 2, 2);
                }

                flatIndices[vertex] = (uint)vertex;
            }
        }

        return (flatPositions, flatNormals, flatUvs, flatIndices);
    }

    private static Vector3 Read3(float[] values, uint vertex) =>
        new(values[vertex * 3], values[(vertex * 3) + 1], values[(vertex * 3) + 2]);

    private static void RequireCount(int count, int vertexCount, string attribute)
    {
        if (count != vertexCount)
        {
            throw new GlbFormatException($"The {attribute} accessor has {count} elements, but POSITION has {vertexCount}.");
        }
    }

    private static JsonElement GetArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new GlbFormatException($"The glTF JSON has no '{name}' array where the mesh needs one.");
        }

        return array;
    }

    private static int ToIndex(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var index) || index < 0)
        {
            throw new GlbFormatException("A glTF index is not a non-negative integer.");
        }

        return index;
    }

    private static int GetInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) ? ToIndex(value) : fallback;

    /// <summary>Reads a byte offset or length, which can be above int.MaxValue in a BIN chunk of up to 4 GiB.</summary>
    private static long GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < 0)
        {
            throw new GlbFormatException($"The glTF property '{name}' is not a non-negative integer.");
        }

        return number;
    }

    /// <summary>Reads accessors of the embedded buffer, seeking to and reading only the bytes that each accessor covers.</summary>
    private sealed class AccessorReader(Stream stream, JsonElement root, long binOffset, long binLength)
    {
        private readonly JsonElement accessors = GetArray(root, "accessors");
        private readonly JsonElement bufferViews = GetArray(root, "bufferViews");
        private readonly long bufferLength = GetEmbeddedBufferLength(root, binLength);

        public float[] ReadFloats(int accessorIndex, string expectedType, out int count)
        {
            var accessor = GetAccessor(accessorIndex);
            var type = accessor.GetProperty("type").GetString();
            var components = expectedType == "VEC3" ? 3 : 2;
            if (!string.Equals(type, expectedType, StringComparison.OrdinalIgnoreCase))
            {
                throw new GlbFormatException($"Accessor {accessorIndex} has type {type}, and the preview needs {expectedType}.");
            }

            var componentType = GetInt(accessor, "componentType", 0);
            if (componentType != FloatComponent)
            {
                throw new GlbFormatException($"Accessor {accessorIndex} has component type {componentType}, and the preview reads only float vertex data.");
            }

            var (bytes, stride) = ReadRange(accessor, accessorIndex, components * sizeof(float), out count);
            var values = new float[count * components];
            for (var element = 0; element < count; element++)
            {
                var offset = element * stride;
                for (var component = 0; component < components; component++)
                {
                    values[(element * components) + component] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset + (component * sizeof(float))));
                }
            }

            return values;
        }

        public uint[] ReadIndices(int accessorIndex, int vertexCount)
        {
            var accessor = GetAccessor(accessorIndex);
            var type = accessor.GetProperty("type").GetString();
            if (!string.Equals(type, "SCALAR", StringComparison.OrdinalIgnoreCase))
            {
                throw new GlbFormatException($"The index accessor {accessorIndex} has type {type}, not SCALAR.");
            }

            var componentType = GetInt(accessor, "componentType", 0);
            var size = componentType switch
            {
                UnsignedByteComponent => 1,
                UnsignedShortComponent => 2,
                UnsignedIntComponent => 4,
                _ => throw new GlbFormatException($"The index accessor {accessorIndex} has component type {componentType}, not an unsigned integer type."),
            };

            var (bytes, stride) = ReadRange(accessor, accessorIndex, size, out var count);
            var indices = new uint[count];
            for (var i = 0; i < count; i++)
            {
                var span = bytes.AsSpan(i * stride);
                var index = size switch
                {
                    1 => span[0],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(span),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(span),
                };

                if (index >= (uint)vertexCount)
                {
                    throw new GlbFormatException($"The index {index} is out of range for {vertexCount} vertices.");
                }

                indices[i] = index;
            }

            return indices;
        }

        private JsonElement GetAccessor(int index)
        {
            if (index >= accessors.GetArrayLength())
            {
                throw new GlbFormatException($"The file has no accessor {index}.");
            }

            var accessor = accessors[index];
            if (accessor.TryGetProperty("sparse", out _))
            {
                throw new GlbFormatException($"Accessor {index} is sparse, which the preview does not read.");
            }

            return accessor;
        }

        /// <summary>
        /// Checks the accessor and its buffer view against the embedded buffer and reads the bytes from the first
        /// element to the end of the last one. The result holds element i at i * stride.
        /// </summary>
        private (byte[] Bytes, int Stride) ReadRange(JsonElement accessor, int accessorIndex, int elementSize, out int count)
        {
            count = GetInt(accessor, "count", 0);
            if (count == 0)
            {
                throw new GlbFormatException($"Accessor {accessorIndex} has no elements.");
            }

            if (!accessor.TryGetProperty("bufferView", out var viewIndexElement))
            {
                throw new GlbFormatException($"Accessor {accessorIndex} has no buffer view.");
            }

            var viewIndex = ToIndex(viewIndexElement);
            if (viewIndex >= bufferViews.GetArrayLength())
            {
                throw new GlbFormatException($"The file has no buffer view {viewIndex}.");
            }

            var view = bufferViews[viewIndex];
            if (GetInt(view, "buffer", 0) != 0)
            {
                throw new GlbFormatException($"Buffer view {viewIndex} uses an external buffer, which the preview does not read.");
            }

            var viewOffset = GetLong(view, "byteOffset");
            var viewLength = GetLong(view, "byteLength");
            if (viewOffset > bufferLength || viewLength > bufferLength - viewOffset)
            {
                throw new GlbFormatException($"Buffer view {viewIndex} extends past the end of the buffer.");
            }

            var stride = GetInt(view, "byteStride", elementSize);
            if (stride < elementSize || stride > MaxStride || stride % Math.Min(elementSize, sizeof(float)) != 0)
            {
                throw new GlbFormatException($"Buffer view {viewIndex} has a byteStride of {stride}, which does not fit elements of {elementSize} bytes.");
            }

            var accessorOffset = GetLong(accessor, "byteOffset");
            var length = ((long)stride * (count - 1)) + elementSize;
            if (accessorOffset > viewLength || length > viewLength - accessorOffset)
            {
                throw new GlbFormatException($"Accessor {accessorIndex} extends past the end of buffer view {viewIndex}.");
            }

            // The checks above bound the length by the BIN chunk, which is at most 4 GiB, so an array can hold
            // it unless it is above Array.MaxLength.
            if (length > Array.MaxLength)
            {
                throw new GlbFormatException($"Accessor {accessorIndex} is too large to read.");
            }

            var bytes = new byte[length];
            stream.Position = binOffset + viewOffset + accessorOffset;
            try
            {
                stream.ReadExactly(bytes);
            }
            catch (EndOfStreamException exception)
            {
                throw new GlbFormatException($"The file ends inside accessor {accessorIndex}.", exception);
            }

            return (bytes, stride);
        }

        /// <summary>
        /// Gives the byteLength of buffer 0, which must be the BIN chunk of the GLB. A buffer with a uri is a separate
        /// file, which the preview never opens.
        /// </summary>
        private static long GetEmbeddedBufferLength(JsonElement root, long binLength)
        {
            var buffers = GetArray(root, "buffers");
            if (buffers.GetArrayLength() == 0)
            {
                throw new GlbFormatException("The file has no buffer.");
            }

            var buffer = buffers[0];
            if (buffer.TryGetProperty("uri", out _))
            {
                throw new GlbFormatException("Buffer 0 is an external file, which the preview does not read.");
            }

            var length = GetLong(buffer, "byteLength");
            if (length > binLength)
            {
                throw new GlbFormatException($"Buffer 0 declares {length} bytes, but the BIN chunk has only {binLength}.");
            }

            return length;
        }
    }
}
