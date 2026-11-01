using Borea.Preview.Rendering;
using Borea.Preview.Scene;

namespace Borea.App.ViewModels;

/// <summary>The view that knows the camera of the 3D preview, which only the view changes.</summary>
public interface IVehiclePictureSource
{
    /// <summary>What the view shows now, or null when it shows no scene.</summary>
    VehiclePicture? CurrentPicture();
}

/// <summary>
/// One view of a scene to draw as a picture. <paramref name="Camera"/> does not change after it is made, so the picture
/// stays the view of the click while the player picks a file.
/// </summary>
public sealed record VehiclePicture(PreviewScene Scene, OrbitCamera Camera, int Width, int Height);
