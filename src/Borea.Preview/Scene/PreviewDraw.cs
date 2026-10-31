using System.Numerics;

namespace Borea.Preview.Scene;

/// <summary>
/// One mesh placed in the vehicle assembly frame. <paramref name="Transform"/> maps the local frame of
/// the mesh to the vehicle frame in the row-vector order of System.Numerics, so a point is
/// <c>Vector3.Transform(local, Transform)</c>.
/// </summary>
public sealed record PreviewDraw(PreviewMesh Mesh, PreviewMaterial Material, Matrix4x4 Transform)
{
    public PreviewBounds Bounds => Mesh.LocalBounds.Transform(Transform);
}
