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
    /// <param name="progress">
    /// Each operation's reports, with <see cref="InstallProgress.Step"/> and
    /// <see cref="InstallProgress.StepCount"/> set to its place in the plan.
    /// </param>
    /// <param name="stop">Stops the operations at a safe point, under the rule of <see cref="InstallStop"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// The plan has unresolved choices or conflicts, the instance no longer
    /// exists, or the instance changed after planning or between operations.
    /// </exception>
    /// <exception cref="InstallStoppedException">The stop ended the plan before its last operation finished.</exception>
    Task ExecuteAsync(
        InstallPlan plan,
        bool enable,
        IProgress<InstallProgress>? progress = null,
        InstallStop? stop = null,
        CancellationToken cancellationToken = default);
}
