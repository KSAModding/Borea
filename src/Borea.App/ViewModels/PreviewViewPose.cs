namespace Borea.App.ViewModels;

/// <summary>
/// Where the player turned, zoomed and moved the camera of a 3D preview. It is separate from the scene, so a second
/// view of the same scene can start from the pose of the first.
/// </summary>
/// <param name="OrbitX">The turn around the vehicle axis, in drag pixels.</param>
/// <param name="OrbitY">The lift toward the vehicle axis, in drag pixels.</param>
/// <param name="ZoomSteps">The zoom, in steps away from the framed distance.</param>
/// <param name="AxisOffset">How far the target moved up the vehicle axis from the center of the vehicle.</param>
public readonly record struct PreviewViewPose(float OrbitX, float OrbitY, float ZoomSteps, float AxisOffset);
