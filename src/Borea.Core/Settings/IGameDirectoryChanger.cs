namespace Borea.Core.Settings;

public interface IGameDirectoryChanger
{
    Task ChangeAsync(string gameDirectory, CancellationToken cancellationToken = default);
}
