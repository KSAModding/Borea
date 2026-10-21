using System.Numerics;
using Borea.Preview.Scene;

namespace Borea.Preview.Tests.Scene;

public sealed class PreviewBoundsTests
{
    [Fact]
    public void SceneBounds_HoldATranslatedScaledDrawAndAPlaceholder()
    {
        var box = PreviewTestScenes.CreateBox();
        var draw = new PreviewDraw(box, PreviewMaterial.Flat(Vector3.One), Matrix4x4.CreateScale(2f, 4f, 6f) * Matrix4x4.CreateTranslation(10f, 0f, 0f));
        var placeholder = new PreviewPlaceholder(Matrix4x4.CreateTranslation(0f, -5f, 1f), new Vector3(1f, 0.5f, 0.25f), "Missing", PreviewPlaceholderReason.UnknownPart);

        var scene = new PreviewScene([draw], [placeholder]);

        // The draw spans 9 to 11, -2 to 2 and -3 to 3, the placeholder -1 to 1, -5.5 to -4.5 and 0.75 to 1.25.
        Assert.Equal(new Vector3(-1f, -5.5f, -3f), scene.Bounds.Min);
        Assert.Equal(new Vector3(11f, 2f, 3f), scene.Bounds.Max);
    }
}
