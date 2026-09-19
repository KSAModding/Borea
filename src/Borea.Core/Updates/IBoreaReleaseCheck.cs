namespace Borea.Core.Updates;

/// <summary>Finds the published Borea releases.</summary>
public interface IBoreaReleaseCheck
{
    /// <summary>The published releases in <paramref name="channel"/>, newest first, or an empty list when there is none or the check fails. It never throws for a failed check.</summary>
    Task<IReadOnlyList<BoreaRelease>> GetReleasesAsync(BoreaUpdateChannel channel = BoreaUpdateChannel.Stable, CancellationToken cancellationToken = default);
}
