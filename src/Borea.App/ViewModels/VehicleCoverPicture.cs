using System;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.Preview;
using Borea.Core.Sharing;
using Borea.Preview.Loading;
using Borea.Preview.Rendering;
using Borea.Preview.Scene;

namespace Borea.App.ViewModels;

/// <summary>Draws the picture of a vehicle folder that a share offers as a cover, with the same scene and renderer as the 3D preview.</summary>
internal static class VehicleCoverPicture
{
    /// <summary>The side of the square picture. A host makes a square icon from a cover, so a square frame keeps the whole vehicle in it.</summary>
    internal const int Size = 1024;

    /// <summary>The color of the preview panel in the dark theme. A public picture is opaque, because Borea does not know what a page shows behind it.</summary>
    private static readonly Rgba8 Background = new(0x26, 0x26, 0x26, 0xFF);

    /// <summary>
    /// Gives the PNG bytes, or null when no picture can stand for the vehicle. A box for a part that did not resolve must not go public, so a scene with one gives no picture.
    /// A failure or a cancel also gives null, because the share goes on without a cover. The files stay in use until this task ends.
    /// </summary>
    public static async Task<byte[]?> DrawAsync(IVehiclePreview service, PreviewSceneBuilder builder, IPngEncoder encoder, string vehicleFolder, Guid instanceId, CancellationToken token)
    {
        try
        {
            using var result = await service.LoadFolderAsync(vehicleFolder, instanceId, token);
            if (!result.IsReady)
                return null;

            var options = new PreviewLoadOptions();
            var (geometry, _) = await builder.BuildGeometryAsync(result.Vehicle, result.Assets, options, token);
            if (geometry.Draws.Count == 0 || geometry.Placeholders.Count > 0)
                return null;

            var (textured, _) = await builder.AddTexturesAsync(geometry, result.Vehicle, result.Assets, options, token);
            var scene = await builder.AddNormalsAsync(textured, result.Vehicle, result.Assets, options, token);
            var png = await Task.Run(() => encoder.Encode(Render(scene, token)), token);
            return png.Length <= ShareImageLimits.MaxBytes ? png : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Renders from the default camera angles at the quality of a view at rest, on a renderer of its own that is dropped after this frame.</summary>
    internal static FrameBuffer Render(PreviewScene scene, CancellationToken token)
    {
        var camera = new OrbitCamera();
        camera.Frame(scene.Bounds, 1f);
        var frame = new FrameBuffer(Size, Size);
        new SoftwareRenderer().Render(scene, camera, frame, new RenderOptions { Background = Background, Quality = RenderQuality.High, WorldCacheBytes = 0, WorkBufferBytes = 0 }, token);
        return frame;
    }
}
