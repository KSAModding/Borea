using System.Numerics;
using System.Xml;

namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// A Transform element as the game keeps it in TransformReference, with each of Position, Rotation and Scale
/// either present or missing. Rotation holds the X, Y and Z angles in radians as written in the file.
/// </summary>
/// <remarks>
/// The game reads the values as double, and this type keeps them as float because the preview draws in float.
/// System.Numerics uses the same matrix layout and the same row-vector order as the Brutal.Numerics double4x4 of
/// the game (translation in the fourth row, a point is transformed as v * M), so a child matrix composes as
/// child * parent, as in Part.MatrixAsmb2VehicleAsmb.
/// </remarks>
internal readonly record struct KsaTransform(Vector3? Position, Vector3? Rotation, Vector3? Scale)
{
    public static KsaTransform Identity => default;

    /// <summary>The rotation as TransformReference.RotationValue gives it, identity when Rotation is missing.</summary>
    public Quaternion RotationQuaternion => Rotation is { } angles ? FromXyzRadians(angles) : Quaternion.Identity;

    /// <summary>
    /// Reads the Transform element that the reader is on, and leaves the reader on its end tag, or on the element
    /// itself when it is empty. A missing X, Y or Z attribute is 0, as in Vector3Reference, so a Scale element with
    /// only X="2" gives (2, 0, 0). When a child element occurs twice, the first one counts, because XmlSerializer
    /// reads a single member only once.
    /// </summary>
    /// <exception cref="FormatException">A component is not a number in the XML double format.</exception>
    public static KsaTransform Parse(XmlReader reader)
    {
        if (reader.NodeType != XmlNodeType.Element)
            throw new InvalidOperationException("The reader must be on the Transform element.");

        Vector3? position = null;
        Vector3? rotation = null;
        Vector3? scale = null;

        using var subtree = reader.ReadSubtree();
        subtree.Read();

        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element || subtree.Depth != 1)
                continue;

            switch (subtree.LocalName)
            {
                case KsaPartSchema.Position when position is null:
                    position = ReadVector(subtree);
                    break;
                case KsaPartSchema.Rotation when rotation is null:
                    rotation = ReadVector(subtree);
                    break;
                case KsaPartSchema.Scale when scale is null:
                    scale = ReadVector(subtree);
                    break;
            }
        }

        return new KsaTransform(position, rotation, scale);
    }

    /// <summary>
    /// The rotation of QuaternionEx.CreateFromXyzRadians, which equals qZ * qY * qX: the part turns about X first,
    /// then about Y, then about Z. It is not Quaternion.CreateFromYawPitchRoll, which turns in another order.
    /// </summary>
    public static Quaternion FromXyzRadians(Vector3 angles)
    {
        var cx = Math.Cos(angles.X / 2.0);
        var cy = Math.Cos(angles.Y / 2.0);
        var cz = Math.Cos(angles.Z / 2.0);
        var sx = Math.Sin(angles.X / 2.0);
        var sy = Math.Sin(angles.Y / 2.0);
        var sz = Math.Sin(angles.Z / 2.0);

        var w = cx * cy * cz + sx * sy * sz;
        var x = sx * cy * cz - cx * sy * sz;
        var y = cx * sy * cz + sx * cy * sz;
        var z = cx * cy * sz - sx * sy * cz;

        return new Quaternion((float)x, (float)y, (float)z, (float)w);
    }

    /// <summary>
    /// The matrix from the local frame to the parent frame, built the way Part.MatrixAsmb2ParentAsmb and
    /// StaticObject build it: scale, then rotation, then translation.
    /// </summary>
    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale ?? Vector3.One)
        * Matrix4x4.CreateFromQuaternion(RotationQuaternion)
        * Matrix4x4.CreateTranslation(Position ?? Vector3.Zero);

    /// <summary>
    /// Tells if every element of the matrix is a finite number. XmlConvert.ToDouble accepts NaN and INF, a value
    /// above the float range becomes infinite in the cast, and two large scales can overflow when they compose, so a
    /// matrix from author XML must pass this check before it goes into the scene.
    /// </summary>
    public static bool IsFinite(Matrix4x4 matrix)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                if (!float.IsFinite(matrix[row, column]))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Replaces only the components that <paramref name="other"/> has, as TransformReference.Apply does. The caller
    /// keeps the game rule that PartInstance.ApplyGameData drops the override when the instance has no Transform.
    /// </summary>
    public KsaTransform Apply(KsaTransform other) =>
        new(other.Position ?? Position, other.Rotation ?? Rotation, other.Scale ?? Scale);

    private static Vector3 ReadVector(XmlReader reader) =>
        new(ReadComponent(reader, KsaPartSchema.X), ReadComponent(reader, KsaPartSchema.Y), ReadComponent(reader, KsaPartSchema.Z));

    private static float ReadComponent(XmlReader reader, string name)
    {
        var text = reader.GetAttribute(name);
        return text is null ? 0f : (float)XmlConvert.ToDouble(text);
    }
}
