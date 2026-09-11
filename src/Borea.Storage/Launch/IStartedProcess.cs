namespace Borea.Storage.Launch;

/// <summary>
/// A process a starter started. Disposing releases the handle, the process
/// keeps running.
/// </summary>
public interface IStartedProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }
}
