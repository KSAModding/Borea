namespace Borea.Core.Updates;

/// <summary>Finds the newest published Borea release.</summary>
public interface IBoreaReleaseCheck
{
    /// <summary>The newest published release in <paramref name="channel"/>, or null when there is none or the check fails. It never throws for a failed check.</summary>
    Task<BoreaRelease?> GetLatestReleaseAsync(BoreaUpdateChannel channel = BoreaUpdateChannel.Stable, CancellationToken cancellationToken = default);
}
