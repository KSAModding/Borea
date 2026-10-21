using System.Numerics;

namespace Borea.Preview.Scene;

/// <summary>
/// An axis-aligned box in the vehicle assembly frame, in meters. <see cref="Empty"/> holds no point,
/// and every operation on it gives the other operand or an empty box again.
/// </summary>
public readonly record struct PreviewBounds(Vector3 Min, Vector3 Max)
{
    public static PreviewBounds Empty { get; } = new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public Vector3 Center => IsEmpty ? Vector3.Zero : (Min + Max) * 0.5f;

    /// <summary>The radius of the sphere around <see cref="Center"/> that holds the whole box, 0 when empty.</summary>
    public float Radius => IsEmpty ? 0f : (Max - Min).Length() * 0.5f;

    public static PreviewBounds FromCenter(Vector3 center, Vector3 halfSize) => new(center - halfSize, center + halfSize);

    public PreviewBounds Include(Vector3 point) => new(Vector3.Min(Min, point), Vector3.Max(Max, point));

    public PreviewBounds Union(PreviewBounds other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    /// <summary>
    /// The box around the eight transformed corners. It is exact for a scale and a translation, and holds
    /// the transformed box for a rotation too.
    /// </summary>
    public PreviewBounds Transform(Matrix4x4 matrix)
    {
        if (IsEmpty)
        {
            return Empty;
        }

        var result = Empty;
        for (var corner = 0; corner < 8; corner++)
        {
            var point = new Vector3(
                (corner & 1) == 0 ? Min.X : Max.X,
                (corner & 2) == 0 ? Min.Y : Max.Y,
                (corner & 4) == 0 ? Min.Z : Max.Z);
            result = result.Include(Vector3.Transform(point, matrix));
        }

        return result;
    }
}
