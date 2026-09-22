namespace Borea.Core.Game;

/// <summary>
/// Thrown instead of writing into a game installation that does not have the
/// shape Borea expects. It is an <see cref="InvalidOperationException"/>, so
/// every caller that already reports a refused install or a refused manifest
/// change reports this one too.
/// </summary>
public sealed class GameShapeException : InvalidOperationException
{
    public GameShape Shape { get; }

    public GameShapeException(GameShape shape)
        : base(Describe(shape))
    {
        Shape = shape;
    }

    private static string Describe(GameShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        return $"{shape.Subject} does not have the shape Borea expects, so Borea changed nothing. {shape.BrokenText} "
            + "Update Borea, or report this at https://github.com/KSAModding/Borea/issues.";
    }
}
