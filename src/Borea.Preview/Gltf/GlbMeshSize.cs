namespace Borea.Preview.Gltf;

/// <summary>The counts that the accessors of primitive 0 of a glTF mesh declare, read from the JSON chunk only.</summary>
/// <param name="VertexCount">The count of the POSITION accessor.</param>
/// <param name="TriangleCount">The index count, or the vertex count of a non-indexed mesh, divided by 3.</param>
public readonly record struct GlbMeshSize(int VertexCount, int TriangleCount);
