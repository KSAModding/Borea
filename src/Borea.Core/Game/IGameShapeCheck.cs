namespace Borea.Core.Game;

/// <summary>
/// Checks the game against the assumptions Borea makes about it. The
/// installation is checked once per installed build, because it only changes
/// when the game is updated; a profile is checked every time, because Borea and
/// the game both write into it.
/// </summary>
public interface IGameShapeCheck
{
    /// <summary>The installation and the profile the game uses on its own.</summary>
    Task<GameShape> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>The installation and the profile of one instance.</summary>
    Task<GameShape> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
