using Borea.Core.Mods;

namespace Borea.Core.Planning;

/// <summary>
/// Carries out a ready <see cref="InstallPlan"/>, one install or replacement
/// per operation.
/// </summary>
public interface IInstallPlanExecutor
{
    /// <summary>
    /// Runs the operations in order. Each one starts only while the instance
    /// still matches the state the plan, or the operation before it, left the
    /// instance in, and each write checks that state again under the instance
    /// lock. An operation that fails stops the operations after it.
    /// </summary>
    /// <param name="enable">Whether new manifest entries are written enabled.</param>
    /// <exception cref="InvalidOperationException">
    /// The plan has unresolved choices or conflicts, the instance no longer
    /// exists, or the instance changed after planning or between operations.
    /// </exception>
    Task ExecuteAsync(
        InstallPlan plan,
        bool enable,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
