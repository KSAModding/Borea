namespace Borea.Core.Instances;

/// <summary>
/// Lists the settings, HUD layouts, crash dumps and exports the game keeps in an instance.
/// </summary>
public interface IGameDataReader
{
    Task<IReadOnlyList<GameDataEntry>> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
