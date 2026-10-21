namespace Borea.Preview.Scene;

/// <summary>
/// Everything the renderer draws for one vehicle, in the vehicle assembly frame in meters. The vehicle
/// points along +X, which the camera shows as screen up.
/// </summary>
public sealed class PreviewScene
{
    public PreviewScene(IReadOnlyList<PreviewDraw> draws, IReadOnlyList<PreviewPlaceholder> placeholders)
    {
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(placeholders);
        Draws = draws;
        Placeholders = placeholders;

        var bounds = PreviewBounds.Empty;
        foreach (var draw in draws)
        {
            bounds = bounds.Union(draw.Bounds);
        }

        foreach (var placeholder in placeholders)
        {
            bounds = bounds.Union(placeholder.Bounds);
        }

        Bounds = bounds;
    }

    public static PreviewScene Empty { get; } = new([], []);

    public IReadOnlyList<PreviewDraw> Draws { get; }

    public IReadOnlyList<PreviewPlaceholder> Placeholders { get; }

    public PreviewBounds Bounds { get; }

    public long TriangleCount => Draws.Sum(draw => (long)draw.Mesh.TriangleCount);
}
