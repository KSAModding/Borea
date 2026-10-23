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

    [Fact]
    public void AnAxisOffset_MovesTheTargetUpTheVehicleAxis_AndTheVehicleDownTheScreen()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);

        camera.SetAxisOffset(2f);
        var center = Vector4.Transform(new Vector4(Rocket.Center, 1f), camera.ViewProjection(1.5f));

        Assert.Equal(Rocket.Center + Vector3.UnitX * 2f, camera.Target);
        Assert.True(center.Y / center.W < -0.05f);
        Assert.InRange(center.X / center.W, -1e-4f, 1e-4f);
    }

    [Fact]
    public void SetAxisOffset_StopsAtTheLowestAndTheHighestExtent_AndIgnoresAnOffsetThatIsNotFinite()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1f);
        Assert.Equal(6f, camera.AxisReach);

        camera.SetAxisOffset(100f);
        Assert.Equal((6f, Rocket.Max.X), (camera.AxisOffset, camera.Target.X));

        camera.SetAxisOffset(-100f);
        Assert.Equal((-6f, Rocket.Min.X), (camera.AxisOffset, camera.Target.X));

        camera.SetAxisOffset(1f);
        camera.SetAxisOffset(float.NaN);
        camera.SetAxisOffset(float.PositiveInfinity);
        Assert.Equal(1f, camera.AxisOffset);
        Assert.Equal(new Vector3(7f, 0f, 0f), camera.Target);
    }

    [Fact]
    public void EmptyBounds_LeaveNoRoomToMoveTheTarget()
    {
        var camera = new OrbitCamera();
        camera.Frame(PreviewBounds.Empty, 1f);

        camera.SetAxisOffset(3f);

        Assert.Equal((0f, 0f), (camera.AxisReach, camera.AxisOffset));
        Assert.Equal(Vector3.Zero, camera.Target);
    }

    [Fact]
    public void Reset_BringsBackTheFramedTarget()
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1f);
        var framed = (camera.Target, camera.Distance);

        camera.SetAxisOffset(4f);
        camera.Zoom(20f);
        camera.Orbit(80f, -40f);
        camera.Reset();

        Assert.Equal(framed, (camera.Target, camera.Distance));
        Assert.Equal(0f, camera.AxisOffset);
        Assert.Equal((OrbitCamera.DefaultYaw, OrbitCamera.DefaultPitch), (camera.Yaw, camera.Pitch));
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(-13f, 200f, 0f)]
    [InlineData(1000f, -90f, 180f)]
    [InlineData(1000f, 400f, -180f)]
    public void TheNearAndTheFarPlane_HoldTheWholeSphere_ForEveryTargetOnTheAxis(float zoom, float dragX, float dragY)
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        camera.Orbit(dragX, dragY);
        camera.Zoom(zoom);
        for (var t = 0; t <= 20; t++)
        {
            camera.SetAxisOffset(camera.AxisReach * (t / 10f - 1f));
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
    [InlineData(0f)]
    [InlineData(-13f)]
    public void TheNearPlane_CutsNothing_AtTheFramedDistanceAndFarther(float zoom)
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        camera.Zoom(zoom);
        foreach (var offset in new[] { -camera.AxisReach, 0f, camera.AxisReach })
        {
            camera.SetAxisOffset(offset);
            Assert.True(camera.NearPlane > camera.Distance * 0.01f, $"The near plane is at its floor for the offset {offset}.");
        }
    }

    [Theory]
    [InlineData(1000f, 0f, 0f)]
    [InlineData(1000f, 0f, 2000f)]
    [InlineData(-1000f, 500f, -2000f)]
    public void TheMatricesStayFinite_AtTheZoomLimitsWithAMovedTarget(float zoom, float dragX, float dragY)
    {
        var camera = new OrbitCamera();
        camera.Frame(Rocket, 1.5f);
        camera.Orbit(dragX, dragY);
        camera.Zoom(zoom);
        camera.SetAxisOffset(100f);

        var matrix = camera.ViewProjection(1.5f);
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
                Assert.True(float.IsFinite(matrix[row, column]), $"The element {row}, {column} is {matrix[row, column]}.");
        }

        Assert.True(camera.NearPlane > 0f && camera.NearPlane < camera.FarPlane);
    }

    private static Vector3 FibonacciSphere(int index, int count)
    {
        var y = 1f - 2f * (index + 0.5f) / count;
        var ring = MathF.Sqrt(1f - y * y);
        var angle = index * MathF.PI * (3f - MathF.Sqrt(5f));
        return new Vector3(MathF.Cos(angle) * ring, y, MathF.Sin(angle) * ring);
    }
}
