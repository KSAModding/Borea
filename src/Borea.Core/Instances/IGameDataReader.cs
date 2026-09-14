namespace Borea.Core.Instances;

/// <summary>
/// Lists the saves, vehicles, settings and other files the game keeps in an instance.
/// </summary>
public interface IGameDataReader
{
    Task<IReadOnlyList<GameDataEntry>> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
