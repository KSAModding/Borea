using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// Saves <see cref="Instance.LastPlayedAt"/> when a watched launch reports that the game started.
/// </summary>
public sealed class LastPlayedLauncher : ILauncher, IDisposable
{
    private readonly IInstanceRepository _instances;
    private readonly TimeProvider _time;

    public ILauncher Inner { get; }

    public LastPlayedLauncher(ILauncher inner, IInstanceRepository instances, TimeProvider? time = null)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _time = time ?? TimeProvider.System;
    }

    public LaunchResult Launch(Instance instance, ModMetadata? loader) => Inner.Launch(instance, loader);

    public async Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var result = await Inner.WatchStartAsync(instance, started, cancellationToken).ConfigureAwait(false);
        if (!result.Started)
            return result;

        var playedAt = _time.GetUtcNow();
        try
        {
            await _instances.UpdateAsync(instance.InstanceId, saved =>
            {
                saved.RecordPlayed(playedAt);
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // the game runs either way, and the last write of its log still says when it was played
        }

        return result;
    }

    public bool IsRunning(Guid instanceId) => Inner.IsRunning(instanceId);

    public void Dispose()
    {
        if (Inner is IDisposable disposable)
            disposable.Dispose();
    }
}
