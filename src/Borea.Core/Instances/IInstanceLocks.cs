namespace Borea.Core.Instances;

/// <summary>
/// The locks that changes to instances take in this process.
/// </summary>
public interface IInstanceLocks
{
    /// <summary>
    /// Takes the lock of every given instance without waiting, and keeps them
    /// until the result is disposed. Null when a change holds one of them.
    /// </summary>
    IDisposable? TryHold(IEnumerable<Guid> instanceIds);
}
