using System.Numerics;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;

namespace Borea.Preview.Tests.Rendering;

public sealed class OrbitCameraTests
{
    private static readonly PreviewBounds Rocket = new(new Vector3(0f, -1f, -1f), new Vector3(12f, 1f, 1f));

    [Theory]
    [InlineData(16f / 9f, 0f, 0f)]
    [InlineData(0.5f, 0f, 0f)]
    [InlineData(3f, 200f, -150f)]
    public void Frame_KeepsTheBoundingSphereInsideTheFrustum(float aspect, float dragX, float dragY)
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, aspect);
        camera.Orbit(dragX, dragY);
        var viewProjection = camera.ViewProjection(aspect);

        var center = Rocket.Center;
        var radius = Rocket.Radius;
        for (var i = 0; i < 400; i++)
        {
            var point = center + FibonacciSphere(i, 400) * radius;
            var clip = Vector4.Transform(new Vector4(point, 1f), viewProjection);

            Assert.True(clip.W > 0f);
            Assert.InRange(clip.X / clip.W, -1f, 1f);
            Assert.InRange(clip.Y / clip.W, -1f, 1f);
            Assert.InRange(clip.Z / clip.W, 0f, 1f);
        }
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(300f, 0f)]
    [InlineData(-120f, 90f)]
    public void PointAtPlusX_ProjectsAboveTheScreenCenter(float dragX, float dragY)
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        camera.Orbit(dragX, dragY);

        var clip = Vector4.Transform(new Vector4(Rocket.Center + Vector3.UnitX, 1f), camera.ViewProjection(1.5f));

        Assert.True(clip.Y / clip.W > 0.05f);
        Assert.InRange(clip.X / clip.W, -1e-4f, 1e-4f);
    }

    [Theory]
    [InlineData(20f, 0f)]
    [InlineData(0f, 20f)]
    public void TheNearSideFollowsTheDrag(float dragX, float dragY)
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        var front = camera.Target + Vector3.Normalize(camera.Eye - camera.Target) * camera.Radius;

        camera.Orbit(dragX, dragY);
        var clip = Vector4.Transform(new Vector4(front, 1f), camera.ViewProjection(1.5f));

        // The point starts at the screen center. Screen y is down and NDC y is up, so a drag down gives NDC y below 0.
        Assert.Equal(dragX > 0f, clip.X / clip.W > 0.01f);
        Assert.Equal(dragY > 0f, clip.Y / clip.W < -0.01f);
    }

    [Fact]
    public void ZoomChangesTheDistance_AndResetRestoresTheFrame()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1f);
        var framed = camera.Distance;

        camera.Zoom(3f);
        camera.Orbit(50f, 50f);
        Assert.True(camera.Distance < framed);

        camera.Reset();
        Assert.Equal(framed, camera.Distance);
        Assert.Equal(OrbitCamera.DefaultYaw, camera.Yaw);
        Assert.Equal(OrbitCamera.DefaultPitch, camera.Pitch);
    }

    [Fact]
    public void ZoomStopsAtTheNearAndTheFarLimit()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        var framed = camera.Distance;

        camera.Zoom(1000f);
        Assert.Equal(framed * OrbitCamera.MinZoomFactor, camera.Distance, 1e-4f);
        Assert.Equal(0.02f, OrbitCamera.MinZoomFactor);

        camera.Zoom(-1000f);
        Assert.Equal(framed * OrbitCamera.MaxZoomFactor, camera.Distance, 1e-3f);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 37f, -21f)]
    [InlineData(-3f, 140f, -60f, -80f, 45f)]
    [InlineData(10f, -250f, 120f, 5f, 90f)]
    public void Pan_KeepsThePointAtTheTargetDepthUnderThePointer(float zoom, float dragX, float dragY, float panX, float panY)
    {
        const float width = 900f;
        const float height = 600f;
        var camera = new OrbitCamera();
        camera.Frame(Rocket, width / height);
        camera.Orbit(dragX, dragY);
        camera.Zoom(zoom);

        // a point at the depth of the target, away from the screen center
        var point = camera.Target + camera.ScreenRight * camera.ViewHeight * 0.2f - camera.ScreenUp * camera.ViewHeight * 0.1f;
        var before = Pixel(camera, point, width, height);

        camera.SetShift(camera.Shift + camera.PanOffset(panX, panY, height));
        var after = Pixel(camera, point, width, height);

        Assert.Equal(before.X + panX, after.X, 0.05f);
        Assert.Equal(before.Y + panY, after.Y, 0.05f);
    }

    [Fact]
    public void PanOffset_IsZeroForAViewWithoutHeight()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1f);

        Assert.Equal(Vector3.Zero, camera.PanOffset(10f, 10f, 0f));
        Assert.Equal(Vector3.Zero, camera.PanOffset(10f, 10f, float.NaN));
    }

    [Fact]
    public void AShiftAlongPlusX_MovesTheTargetUpTheScreenAtTheDefaultPose()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        Assert.True(Vector3.Dot(camera.ScreenUp, Vector3.UnitX) > 0.9f);

        camera.SetShift(Vector3.UnitX * 2f);
        var center = Vector4.Transform(new Vector4(Rocket.Center, 1f), camera.ViewProjection(1.5f));

        Assert.Equal(Rocket.Center + Vector3.UnitX * 2f, camera.Target);
        Assert.True(center.Y / center.W < -0.05f);
        Assert.InRange(center.X / center.W, -1e-4f, 1e-4f);
    }

    [Fact]
    public void SetShift_StaysInsideTheFramedSphere_AndIgnoresAShiftThatIsNotFinite()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1f);
        var direction = Vector3.Normalize(new Vector3(3f, -1f, 2f));

        camera.SetShift(direction * 100f);
        Assert.Equal(camera.Radius, camera.Shift.Length(), 1e-3f);
        Assert.True(Vector3.Distance(direction, Vector3.Normalize(camera.Shift)) < 1e-5f);

        camera.SetShift(Vector3.UnitX);
        camera.SetShift(new Vector3(float.NaN, 0f, 0f));
        camera.SetShift(new Vector3(0f, float.PositiveInfinity, 0f));
        Assert.Equal(Vector3.UnitX, camera.Shift);
    }

    [Fact]
    public void Reset_BringsBackTheFramedTarget()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1f);
        var framed = (camera.Target, camera.Distance);

        camera.SetShift(new Vector3(4f, 0.5f, 0f));
        camera.Zoom(20f);
        camera.Orbit(80f, -40f);
        camera.Reset();

        Assert.Equal(framed, (camera.Target, camera.Distance));
        Assert.Equal(Vector3.Zero, camera.Shift);
        Assert.Equal((OrbitCamera.DefaultYaw, OrbitCamera.DefaultPitch), (camera.Yaw, camera.Pitch));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-13f)]
    [InlineData(1000f)]
    public void TheNearAndTheFarPlane_HoldTheWholeSphere_ForATargetOnTheSphere(float zoom)
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        camera.Zoom(zoom);
        for (var t = 0; t < 40; t++)
        {
            camera.SetShift(FibonacciSphere(t, 40) * camera.Radius);
            var view = camera.View;
            for (var i = 0; i < 400; i++)
            {
                var depth = -Vector3.Transform(Rocket.Center + FibonacciSphere(i, 400) * Rocket.Radius, view).Z;
                Assert.True(depth <= camera.FarPlane * 1.0001f, $"A point at depth {depth} is past the far plane {camera.FarPlane}.");

                // Only a point nearer to the eye than the near plane is cut, and the near plane is at 1 percent of the distance then.
                if (depth < camera.NearPlane * 0.9999f)
                    Assert.Equal(camera.Distance * 0.01f, camera.NearPlane, camera.Distance * 1e-5f);
            }
        }
    }

    [Theory]
    [InlineData(1000f, 0f, 0f)]
    [InlineData(1000f, 0f, 2000f)]
    [InlineData(-1000f, 500f, -2000f)]
    public void TheMatricesStayFinite_AtTheZoomLimitsWithAShiftedTarget(float zoom, float dragX, float dragY)
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        camera.Orbit(dragX, dragY);
        camera.Zoom(zoom);
        camera.SetShift(new Vector3(100f, 3f, -2f));

        var matrix = camera.ViewProjection(1.5f);
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
                Assert.True(float.IsFinite(matrix[row, column]), $"The element {row}, {column} is {matrix[row, column]}.");
        }

        Assert.True(camera.NearPlane > 0f && camera.NearPlane < camera.FarPlane);
    }

    /// <summary>The pixel of <paramref name="point"/> with screen y down, in a view of the given size.</summary>
    private static Vector2 Pixel(OrbitCamera camera, Vector3 point, float width, float height)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), camera.ViewProjection(width / height));
        return new Vector2((clip.X / clip.W + 1f) * 0.5f * width, (1f - clip.Y / clip.W) * 0.5f * height);
    }

    private static Vector3 FibonacciSphere(int index, int count)
    {
        var y = 1f - 2f * (index + 0.5f) / count;
        var ring = MathF.Sqrt(1f - y * y);
        var angle = index * MathF.PI * (3f - MathF.Sqrt(5f));
        return new Vector3(MathF.Cos(angle) * ring, y, MathF.Sin(angle) * ring);
    }
}
