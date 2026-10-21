using System.Numerics;

namespace Borea.Preview.Scene;

/// <summary>Hand-built scenes that let the renderer and the viewer run without game files.</summary>
internal static class PreviewTestScenes
{
    /// <summary>
    /// Three boxes stacked along +X that get narrower toward the top, like a small rocket, and one
    /// placeholder on top of them.
    /// </summary>
    public static PreviewScene StackedBoxes()
    {
        var box = CreateBox();
        var draws = new[]
        {
            new PreviewDraw(box, PreviewMaterial.Flat(new Vector3(0.8f, 0.8f, 0.8f)), Place(new Vector3(2f, 1.5f, 1.5f), new Vector3(1f, 0f, 0f))),
            new PreviewDraw(box, PreviewMaterial.Flat(new Vector3(0.8f, 0.3f, 0.2f)), Place(new Vector3(1.5f, 1.2f, 1.2f), new Vector3(2.75f, 0f, 0f))),
            new PreviewDraw(box, PreviewMaterial.Flat(new Vector3(0.2f, 0.4f, 0.8f)), Place(new Vector3(1f, 0.8f, 0.8f), new Vector3(4f, 0f, 0f))),
        };
        var placeholders = new[]
        {
            new PreviewPlaceholder(Matrix4x4.CreateTranslation(5f, 0f, 0f), new Vector3(0.5f, 0.4f, 0.4f), "TestMissingPart", PreviewPlaceholderReason.UnknownPart),
        };
        return new PreviewScene(draws, placeholders);
    }

    /// <summary>
    /// A box from -0.5 to 0.5 on each axis with 24 vertices, so each face has its own normal and UVs.
    /// The faces are counterclockwise when seen from outside.
    /// </summary>
    public static PreviewMesh CreateBox()
    {
        var positions = new List<float>(72);
        var normals = new List<float>(72);
        var uvs = new List<float>(48);
        var indices = new List<uint>(36);

        for (var axis = 0; axis < 3; axis++)
        {
            foreach (var sign in new[] { 1f, -1f })
            {
                var normal = Axis(axis) * sign;
                var u = Axis((axis + 1) % 3) * sign;
                var v = Axis((axis + 2) % 3);
                var first = (uint)(positions.Count / 3);
                foreach (var (du, dv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var corner = (normal + u * du + v * dv) * 0.5f;
                    positions.AddRange([corner.X, corner.Y, corner.Z]);
                    normals.AddRange([normal.X, normal.Y, normal.Z]);
                    uvs.AddRange([(du + 1f) * 0.5f, (1f - dv) * 0.5f]);
                }

                indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            }
        }

        return new PreviewMesh([.. positions], [.. normals], [.. uvs], [.. indices]);
    }

    private static Vector3 Axis(int axis) => axis switch
    {
        0 => Vector3.UnitX,
        1 => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };

    private static Matrix4x4 Place(Vector3 size, Vector3 center) => Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center);
}
