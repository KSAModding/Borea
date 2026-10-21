using System.Numerics;
using Borea.Preview.Scene;

namespace Borea.Preview.Rendering;

/// <summary>
/// A camera that circles a target at a fixed field of view. The vehicle axis +X is screen up, so the yaw
/// turns the camera around +X and the pitch lifts it toward +X. The matrices are right-handed with the
/// row-vector order of System.Numerics, and the projection maps depth to 0 at the near plane and 1 at
/// the far plane.
/// </summary>
public sealed class OrbitCamera
{
    /// <summary>The vertical field of view in radians, 40 degrees.</summary>
    public const float FieldOfViewY = 40f * MathF.PI / 180f;

    public const float DefaultYaw = 30f * MathF.PI / 180f;

    public const float DefaultPitch = 15f * MathF.PI / 180f;

    /// <summary>The pitch stays below this many radians from the horizon, so the view never looks along +X.</summary>
    public const float MaxPitch = 85f * MathF.PI / 180f;

    public const float RadiansPerPixel = 0.008f;

    /// <summary>The distance changes by this factor for each zoom step.</summary>
    public const float ZoomStepFactor = 0.9f;

    /// <summary>The frame distance is this much larger than the distance at which the sphere touches the view.</summary>
    public const float FrameMargin = 1.1f;

    private float _framedDistance;

    public OrbitCamera()
    {
        Frame(PreviewBounds.Empty, 1f);
    }

    public Vector3 Target { get; private set; }

    /// <summary>The radius of the framed bounding sphere, at least a small positive value.</summary>
    public float Radius { get; private set; }

    public float Yaw { get; private set; }

    public float Pitch { get; private set; }

    public float Distance { get; private set; }

    public Vector3 Eye => Target + Direction * Distance;

    public float NearPlane => MathF.Max(Distance - 2f * Radius, Distance * 0.01f);

    public float FarPlane => Distance + 2f * Radius;

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitX);

    /// <summary>The unit vector from the target to the eye.</summary>
    private Vector3 Direction
    {
        get
        {
            var horizontal = Vector3.UnitZ * MathF.Cos(Yaw) + Vector3.UnitY * MathF.Sin(Yaw);
            return Vector3.UnitX * MathF.Sin(Pitch) + horizontal * MathF.Cos(Pitch);
        }
    }

    /// <param name="aspect">The width of the view divided by its height.</param>
    public Matrix4x4 Projection(float aspect) => Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewY, ValidAspect(aspect), NearPlane, FarPlane);

    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);

    /// <summary>
    /// Aims at the center of <paramref name="bounds"/> from the default angles, at the distance where the
    /// bounding sphere fits inside the narrower of the two fields of view with a small margin.
    /// </summary>
    public void Frame(PreviewBounds bounds, float aspect)
    {
        Target = bounds.Center;
        Radius = bounds.IsEmpty ? 1f : MathF.Max(bounds.Radius, 0.01f);

        var halfVertical = FieldOfViewY * 0.5f;
        var halfHorizontal = MathF.Atan(MathF.Tan(halfVertical) * ValidAspect(aspect));
        var halfNarrow = MathF.Min(halfVertical, halfHorizontal);
        _framedDistance = Radius / MathF.Sin(halfNarrow) * FrameMargin;
        Reset();
    }

    /// <summary>Goes back to the angles and the distance of the last <see cref="Frame"/>.</summary>
    public void Reset()
    {
        Yaw = DefaultYaw;
        Pitch = DefaultPitch;
        Distance = _framedDistance;
    }

    /// <summary>
    /// Turns the camera by a drag in pixels with screen y down, so the vehicle follows the pointer: a drag
    /// down shows more of the top.
    /// </summary>
    public void Orbit(float dx, float dy)
    {
        var yaw = Yaw - dx * RadiansPerPixel;
        Yaw = MathF.IEEERemainder(yaw, 2f * MathF.PI);
        Pitch = Math.Clamp(Pitch + dy * RadiansPerPixel, -MaxPitch, MaxPitch);
    }

    /// <summary>Moves closer for positive steps and away for negative steps, within 0.15 to 4 times the frame distance.</summary>
    public void Zoom(float steps)
    {
        Distance = Math.Clamp(Distance * MathF.Pow(ZoomStepFactor, steps), _framedDistance * 0.15f, _framedDistance * 4f);
    }

    private static float ValidAspect(float aspect) => float.IsFinite(aspect) && aspect > 0f ? aspect : 1f;
}
