using System.Numerics;
using Borea.Preview.Scene;

namespace Borea.Preview.Rendering;

/// <summary>
/// A camera that circles a target at a fixed field of view. The vehicle axis +X is screen up, so the yaw
/// turns the camera around +X and the pitch lifts it toward +X. The target starts at the center of the framed
/// bounding sphere and can move inside that sphere, so the player can look closely at one part of a tall
/// vehicle. The matrices are right-handed with the row-vector order of System.Numerics, and the projection
/// maps depth to 0 at the near plane and 1 at the far plane.
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

    /// <summary>
    /// The target stays inside the framed sphere, so every point of the sphere is at most 2 <see cref="Radius"/>
    /// from the target, and its depth from the eye is between <see cref="Distance"/> minus and plus 2 Radius. The
    /// near and the far plane thus hold the whole vehicle for every target. When the eye comes into the sphere,
    /// the near plane stays at 1 percent of the distance, and only parts nearer to the eye than that are cut.
    /// </summary>
    public float NearPlane => MathF.Max(Distance - 2f * Radius, Distance * MinNearFraction);

    public float FarPlane => Distance + 2f * Radius;

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitX);

    /// <summary>How far the target is from the center of the framed sphere, in the vehicle frame. Its length is at most <see cref="Radius"/>.</summary>
    public Vector3 Shift => Target - _framedCenter;

    /// <summary>The unit vector that points to the right on the screen, in the vehicle frame.</summary>
    public Vector3 ScreenRight => Vector3.Normalize(Vector3.Cross(Vector3.UnitX, Direction));

    /// <summary>The unit vector that points up on the screen, in the vehicle frame.</summary>
    public Vector3 ScreenUp => Vector3.Cross(Direction, ScreenRight);

    /// <summary>The height that the view shows at the depth of the target, in meters.</summary>
    public float ViewHeight => 2f * Distance * MathF.Tan(FieldOfViewY * 0.5f);

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
    /// are empty or not finite give a sphere of 1 m around the origin, so the matrices always stay finite.
    /// </summary>
    public void Frame(PreviewBounds bounds, float aspect)
    {
        var valid = bounds.IsFinite;
        _framedCenter = valid ? bounds.Center : Vector3.Zero;
        Radius = valid ? MathF.Max(bounds.Radius, 0.01f) : 1f;

        var halfVertical = FieldOfViewY * 0.5f;
        var halfHorizontal = MathF.Atan(MathF.Tan(halfVertical) * ValidAspect(aspect));
        var halfNarrow = MathF.Min(halfVertical, halfHorizontal);
        _framedDistance = Radius / MathF.Sin(halfNarrow) * FrameMargin;
        Reset();
    }

    /// <summary>Goes back to the target, the angles and the distance of the last <see cref="Frame"/>.</summary>
    public void Reset()
    {
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
    /// Puts the target at <paramref name="shift"/> from the center of the framed sphere, in the vehicle frame. A
    /// shift longer than <see cref="Radius"/> is shortened to the radius, so <see cref="NearPlane"/> and
    /// <see cref="FarPlane"/> hold the vehicle for every target. The sphere is much wider than a tall, thin vehicle,
    /// so a close view can still move past the hull and show only the background until <see cref="Reset"/>.
    /// A shift that is not finite leaves the target.
    /// </summary>
    public void SetShift(Vector3 shift)
    {
        if (!float.IsFinite(shift.X) || !float.IsFinite(shift.Y) || !float.IsFinite(shift.Z))
            return;

        var length = shift.Length();
        Target = _framedCenter + (length > Radius ? shift * (Radius / length) : shift);
    }

    /// <summary>
    /// The change of <see cref="Shift"/> that keeps the point at the depth of the target under a pointer that
    /// moves by <paramref name="dx"/> and <paramref name="dy"/> pixels, with screen y down, in a view that is
    /// <paramref name="heightPixels"/> high. The vehicle follows the pointer, so the target moves the other way.
    /// </summary>
    public Vector3 PanOffset(float dx, float dy, float heightPixels)
    {
        if (!float.IsFinite(heightPixels) || heightPixels <= 0f)
            return Vector3.Zero;

        return (ScreenUp * dy - ScreenRight * dx) * (ViewHeight / heightPixels);
    }

    private static float ValidAspect(float aspect) => float.IsFinite(aspect) && aspect > 0f ? aspect : 1f;
}
