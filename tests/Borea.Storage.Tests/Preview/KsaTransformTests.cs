using System.Numerics;
using System.Xml;
using Borea.Storage.Preview.Ksa;

namespace Borea.Storage.Tests.Preview;

public sealed class KsaTransformTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void FromXyzRadians_TurnsAboutXThenYThenZ_NotAsYawPitchRoll()
    {
        var angles = new Vector3(0.3f, -0.7f, 1.1f);
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, angles.X);
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angles.Y);
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angles.Z);

        var actual = KsaTransform.FromXyzRadians(angles);

        AssertSameRotation(qz * qy * qx, actual);
        var yawPitchRoll = Quaternion.CreateFromYawPitchRoll(angles.Y, angles.X, angles.Z);
        Assert.True(MathF.Abs(Quaternion.Dot(yawPitchRoll, actual)) < 0.999f);
    }

    [Fact]
    public void Parse_ScaleWithOnlyX_GivesZeroForTheMissingComponents()
    {
        var transform = Parse("<Transform><Scale X=\"2\"/></Transform>");

        Assert.Equal(new Vector3(2, 0, 0), transform.Scale);
        Assert.Null(transform.Position);
        Assert.Null(transform.Rotation);
    }

    [Fact]
    public void ToMatrix_MissingTransform_IsIdentity()
    {
        Assert.Equal(Matrix4x4.Identity, KsaTransform.Identity.ToMatrix());
    }

    [Fact]
    public void Parse_MissingRotation_GivesIdentityRotation()
    {
        var transform = Parse("<Transform><Position X=\"1\" Y=\"2\" Z=\"3\"/></Transform>");

        Assert.Equal(Quaternion.Identity, transform.RotationQuaternion);
        Assert.Equal(Matrix4x4.CreateTranslation(1, 2, 3), transform.ToMatrix());
    }

    [Fact]
    public void ToMatrix_LocalTimesParent_PlacesThePointInTheParentFrame()
    {
        var parent = new KsaTransform(new Vector3(10, 0, 0), new Vector3(0, 0, MathF.PI / 2), null);
        var local = new KsaTransform(new Vector3(1, 0, 0), null, new Vector3(2, 2, 2));

        var point = Vector3.Transform(new Vector3(0.5f, 0, 0), local.ToMatrix() * parent.ToMatrix());

        Assert.Equal(10f, point.X, Tolerance);
        Assert.Equal(2f, point.Y, Tolerance);
        Assert.Equal(0f, point.Z, Tolerance);
    }

    [Fact]
    public void Apply_OnlyPosition_KeepsRotationAndScale()
    {
        var target = new KsaTransform(new Vector3(1, 1, 1), new Vector3(0.1f, 0.2f, 0.3f), new Vector3(3, 3, 3));

        var result = target.Apply(new KsaTransform(new Vector3(5, 6, 7), null, null));

        Assert.Equal(new KsaTransform(new Vector3(5, 6, 7), new Vector3(0.1f, 0.2f, 0.3f), new Vector3(3, 3, 3)), result);
    }

    private static KsaTransform Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        reader.MoveToContent();
        return KsaTransform.Parse(reader);
    }

    private static void AssertSameRotation(Quaternion expected, Quaternion actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
        Assert.Equal(expected.W, actual.W, Tolerance);
    }
}
