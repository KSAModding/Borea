namespace Borea.Core.Game;

/// <summary>The one place a write path asks whether it may write.</summary>
internal static class GameShapeGate
{
    /// <summary>For a write into the profile of one instance.</summary>
    public static async Task RequireAsync(IGameShapeCheck check, Guid instanceId, CancellationToken cancellationToken)
        => Require(await check.GetForInstanceAsync(instanceId, cancellationToken).ConfigureAwait(false));

    /// <summary>For work that reads or writes the profile the game keeps itself.</summary>
    public static async Task RequireAsync(IGameShapeCheck check, CancellationToken cancellationToken)
        => Require(await check.GetAsync(cancellationToken).ConfigureAwait(false));

    private static void Require(GameShape shape)
    {
        if (!shape.AllowsWrites)
            throw new GameShapeException(shape);
    }
}
