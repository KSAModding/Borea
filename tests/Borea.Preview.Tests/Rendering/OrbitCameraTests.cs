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

    private static Vector3 FibonacciSphere(int index, int count)
    {
        var y = 1f - 2f * (index + 0.5f) / count;
        var ring = MathF.Sqrt(1f - y * y);
        var angle = index * MathF.PI * (3f - MathF.Sqrt(5f));
        return new Vector3(MathF.Cos(angle) * ring, y, MathF.Sin(angle) * ring);
    }
}
