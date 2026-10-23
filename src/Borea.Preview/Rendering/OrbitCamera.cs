using System.Numerics;
using Borea.Preview.Scene;

namespace Borea.Preview.Rendering;

/// <summary>
/// A camera that circles a target at a fixed field of view. The vehicle axis +X is screen up, so the yaw
/// turns the camera around +X and the pitch lifts it toward +X. The target starts at the center of the framed
/// bounds and moves only along +X, between the lowest and the highest extent of the bounds, as the camera of
/// the game editor does. The player can thus look closely at one stage of a tall vehicle, and the view always
/// aims at a point on the vehicle axis inside the bounds. The matrices are right-handed with the row-vector
/// order of System.Numerics, and the projection maps depth to 0 at the near plane and 1 at the far plane.
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

    /// <summary>The closest zoom as a fraction of the frame distance, near enough that one stage of a tall vehicle fills the view.</summary>
    public const float MinZoomFactor = 0.02f;

    /// <summary>The farthest zoom as a multiple of the frame distance.</summary>
    public const float MaxZoomFactor = 4f;

    /// <summary>The near plane stays at this fraction of the distance at least, so the depth range stays finite when the eye is inside the sphere.</summary>
    private const float MinNearFraction = 0.01f;

    private float _framedDistance;
    private Vector3 _framedCenter;

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

    /// <summary>The position of the target on the vehicle axis, in meters along +X from the framed center.</summary>
    public float AxisOffset { get; private set; }

    /// <summary>The largest <see cref="AxisOffset"/> each way, half the length of the framed bounds along +X.</summary>
    public float AxisReach { get; private set; }

    /// <summary>
    /// Every point of the framed sphere is at most <see cref="Radius"/> plus the length of <see cref="AxisOffset"/>
    /// from the target, so its depth from the eye is inside <see cref="Distance"/> minus and plus that reach. The near
    /// and the far plane thus hold the whole vehicle for every target. When the eye comes that near, the near plane
    /// stays at 1 percent of the distance, and only parts nearer to the eye than that are cut.
    /// </summary>
    public float NearPlane => MathF.Max(Distance - DepthReach, Distance * MinNearFraction);

    public float FarPlane => Distance + DepthReach;

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitX);

    /// <summary>The height that the view shows at the depth of the target, in meters.</summary>
    public float ViewHeight => 2f * Distance * MathF.Tan(FieldOfViewY * 0.5f);

    private float DepthReach => Radius + MathF.Abs(AxisOffset);

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
    /// bounding sphere fits inside the narrower of the two fields of view with a small margin. Bounds that
    /// are empty or not finite give a sphere of 1 m around the origin and no room to move the target, so the
    /// matrices always stay finite.
    /// </summary>
    public void Frame(PreviewBounds bounds, float aspect)
    {
        var valid = bounds.IsFinite;
        _framedCenter = valid ? bounds.Center : Vector3.Zero;
        Radius = valid ? MathF.Max(bounds.Radius, 0.01f) : 1f;
        AxisReach = valid ? (bounds.Max.X - bounds.Min.X) * 0.5f : 0f;

        var halfVertical = FieldOfViewY * 0.5f;
        var halfHorizontal = MathF.Atan(MathF.Tan(halfVertical) * ValidAspect(aspect));
        var halfNarrow = MathF.Min(halfVertical, halfHorizontal);
        _framedDistance = Radius / MathF.Sin(halfNarrow) * FrameMargin;
        Reset();
    }

    /// <summary>Goes back to the target, the angles and the distance of the last <see cref="Frame"/>.</summary>
    public void Reset()
    {
        AxisOffset = 0f;
        Target = _framedCenter;
        Yaw = DefaultYaw;
        Pitch = DefaultPitch;
        Distance = _framedDistance;
    }

    /// <summary>
    /// Turns the camera by a drag in pixels with screen y down, so the near side of the vehicle follows the
    /// pointer. A drag to the right turns the near side to the right, and a drag down shows more of the top.
    /// </summary>
    public void Orbit(float dx, float dy)
    {
        var yaw = Yaw + dx * RadiansPerPixel;
        Yaw = MathF.IEEERemainder(yaw, 2f * MathF.PI);
        Pitch = Math.Clamp(Pitch + dy * RadiansPerPixel, -MaxPitch, MaxPitch);
    }

    /// <summary>
    /// Moves closer for positive steps and away for negative steps, within <see cref="MinZoomFactor"/> and
    /// <see cref="MaxZoomFactor"/> times the frame distance.
    /// </summary>
    public void Zoom(float steps)
    {
        Distance = Math.Clamp(Distance * MathF.Pow(ZoomStepFactor, steps), _framedDistance * MinZoomFactor, _framedDistance * MaxZoomFactor);
    }

    /// <summary>
    /// Puts the target at <paramref name="offset"/> meters along +X from the framed center. The offset stops at
    /// <see cref="AxisReach"/> each way, so the target stays between the lowest and the highest extent of the vehicle.
    /// An offset that is not finite leaves the target.
    /// </summary>
    public void SetAxisOffset(float offset)
    {
        if (!float.IsFinite(offset))
            return;

        AxisOffset = Math.Clamp(offset, -AxisReach, AxisReach);
        Target = _framedCenter + Vector3.UnitX * AxisOffset;
    }

    private static float ValidAspect(float aspect) => float.IsFinite(aspect) && aspect > 0f ? aspect : 1f;
}
