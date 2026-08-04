namespace Borea.Core.Game;

/// <summary>
/// Asks the KSA master server for the current public game version.
/// </summary>
public interface ILatestVersionPing
{
    Task<LatestVersionInfo> PingAsync(CancellationToken cancellationToken = default);
}
