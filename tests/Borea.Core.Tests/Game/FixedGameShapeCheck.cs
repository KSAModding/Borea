using Borea.Core.Game;

namespace Borea.Core.Tests.Game;

/// <summary>A check that answers with one shape, whatever it is asked about.</summary>
internal sealed class FixedGameShapeCheck : IGameShapeCheck
{
    private readonly GameShape _shape;

    public FixedGameShapeCheck(GameShape shape)
    {
        _shape = shape;
    }

    /// <summary>A shape whose profile manifest is in a format Borea does not write.</summary>
    public static GameShape Broken { get; } = new(
        null,
        VerifiedGameBuilds.Current,
        [new GameAssumptionResult(GameAssumption.ProfileManifest, GameAssumptionState.Broken, "the manifest did not read as a manifest.")]);

    public static GameShape Holds { get; } = new(
        null,
        VerifiedGameBuilds.Current,
        [new GameAssumptionResult(GameAssumption.ProfileManifest, GameAssumptionState.Holds, "the manifest lists 1 entry.")]);

    public Task<GameShape> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_shape);

    public Task<GameShape> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default) => Task.FromResult(_shape);
}
