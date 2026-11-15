using System;
using System.Threading;
using System.Threading.Tasks;

namespace Borea.App.GameWatch;

/// <summary>
/// Waits for a game that Borea started to end, by polling whether a game process runs.
/// A loader or a wrapper can take some seconds before the game process shows, so the watch first waits for it to appear.
/// </summary>
internal sealed class GameRunWatch(Func<bool> isGameRunning, TimeProvider time)
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    internal static readonly TimeSpan AppearLimit = TimeSpan.FromSeconds(90);

    /// <returns>True when a game ran and ended, false when none appeared within <see cref="AppearLimit"/>.</returns>
    internal async Task<bool> WaitForEndAsync(CancellationToken cancellationToken = default)
    {
        var start = time.GetUtcNow();
        while (!isGameRunning())
        {
            if (time.GetUtcNow() - start >= AppearLimit)
                return false;

            await Task.Delay(PollInterval, time, cancellationToken).ConfigureAwait(false);
        }

        do
            await Task.Delay(PollInterval, time, cancellationToken).ConfigureAwait(false);
        while (isGameRunning());

        return true;
    }
}
