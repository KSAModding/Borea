using System.Numerics;

namespace Borea.Preview.Scene;

/// <summary>
/// Decoded triangle geometry in the local frame of its mesh. Each vertex has three position floats,
/// three normal floats when <see cref="Normals"/> is not empty, and two UV floats when
/// <see cref="Uvs"/> is not null. A front face is counterclockwise when seen from outside. Without
/// normals the renderer shades each triangle flat.
/// </summary>
public sealed class PreviewMesh
{
    /// <exception cref="ArgumentException">An array length does not match the vertex count, or an index is out of range.</exception>
    public PreviewMesh(float[] positions, float[] normals, float[]? uvs, uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(indices);

        if (positions.Length % 3 != 0)
        {
            throw new ArgumentException("The position count is not a multiple of 3.", nameof(positions));
        }

        var vertexCount = positions.Length / 3;
        if (normals.Length != 0 && normals.Length != positions.Length)
        {
            throw new ArgumentException("The normals are neither empty nor one per vertex.", nameof(normals));
        }

        if (uvs is not null && uvs.Length != vertexCount * 2)
        {
            throw new ArgumentException("The UVs are not one pair per vertex.", nameof(uvs));
        }

        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("The index count is not a multiple of 3.", nameof(indices));
        }

        foreach (var index in indices)
        {
            if (index >= (uint)vertexCount)
            {
                throw new ArgumentException($"The index {index} is out of range for {vertexCount} vertices.", nameof(indices));
            }
        }

        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Indices = indices;
        VertexCount = vertexCount;

        var bounds = PreviewBounds.Empty;
        for (var i = 0; i < positions.Length; i += 3)
        {
            bounds = bounds.Include(new Vector3(positions[i], positions[i + 1], positions[i + 2]));
        }

        LocalBounds = bounds;
        SphereCenter = bounds.Center;

        // The radius comes from the vertices, so it is tighter than the half diagonal of the box.
        var radiusSquared = 0f;
        for (var i = 0; i < positions.Length; i += 3)
        {
            var distance = Vector3.DistanceSquared(SphereCenter, new Vector3(positions[i], positions[i + 1], positions[i + 2]));
            radiusSquared = MathF.Max(radiusSquared, distance);
        }

        SphereRadius = MathF.Sqrt(radiusSquared);
    }

    public float[] Positions { get; }

    public float[] Normals { get; }

    public float[]? Uvs { get; }

    public uint[] Indices { get; }

    public int VertexCount { get; }

    public int TriangleCount => Indices.Length / 3;

    public PreviewBounds LocalBounds { get; }

    /// <summary>The center of the local bounding sphere, which is the center of <see cref="LocalBounds"/>.</summary>
    public Vector3 SphereCenter { get; }

    public float SphereRadius { get; }
}
