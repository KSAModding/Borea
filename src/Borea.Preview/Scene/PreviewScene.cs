namespace Borea.Preview.Scene;

/// <summary>
/// Everything the renderer draws for one vehicle, in the vehicle assembly frame in meters. The vehicle
/// points along +X, which the camera shows as screen up. A draw or a placeholder whose bounds are not
/// finite, for example from a NaN in a part transform, is left out of the scene, so one bad transform
/// cannot make the bounds or the camera invalid for all the other draws.
/// </summary>
public sealed class PreviewScene
{
    public PreviewScene(IReadOnlyList<PreviewDraw> draws, IReadOnlyList<PreviewPlaceholder> placeholders)
    {
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(placeholders);
        Draws = [.. draws.Where(draw => draw.Bounds.IsFinite)];
        Placeholders = [.. placeholders.Where(placeholder => placeholder.Bounds.IsFinite)];

        var bounds = PreviewBounds.Empty;
        foreach (var draw in Draws)
        {
            bounds = bounds.Union(draw.Bounds);
        }

        foreach (var placeholder in Placeholders)
        {
            bounds = bounds.Union(placeholder.Bounds);
        }

        // A union of finite boxes can still be too large for a finite radius.
        Bounds = bounds.IsFinite ? bounds : PreviewBounds.Empty;
    }

    public static PreviewScene Empty { get; } = new([], []);

    public IReadOnlyList<PreviewDraw> Draws { get; }

    public IReadOnlyList<PreviewPlaceholder> Placeholders { get; }

    public PreviewBounds Bounds { get; }

    public long TriangleCount => Draws.Sum(draw => (long)draw.Mesh.TriangleCount);
}
