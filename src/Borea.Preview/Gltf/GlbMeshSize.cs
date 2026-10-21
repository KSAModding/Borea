using Borea.Preview.Scene;

namespace Borea.Preview.Gltf;

/// <summary>The counts that the accessors of primitive 0 of a glTF mesh declare, read from the JSON chunk only.</summary>
/// <param name="VertexCount">The count of the POSITION accessor.</param>
/// <param name="TriangleCount">The index count, or the vertex count of a non-indexed mesh, divided by 3.</param>
public readonly record struct GlbMeshSize(int VertexCount, int TriangleCount)
{
    /// <summary>
    /// The box that the min and max of the POSITION accessor declare, or null when the file gives no finite min and max.
    /// glTF requires both on a POSITION accessor, so the scene builder can size a box for a mesh it does not decode.
    /// </summary>
    public PreviewBounds? Bounds { get; init; }
}
