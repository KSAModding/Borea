namespace Borea.Core.Updates;

/// <summary>Replaces the running Borea build with a published release.</summary>
public interface ISelfUpdater
{
    /// <summary>Whether this build may replace itself. Read it before an update is offered.</summary>
    SelfUpdateReadiness GetReadiness();

    /// <summary>
    /// Downloads the archive of <paramref name="release"/> for this build, checks it against the
    /// checksums of the same release, and unpacks it next to this build. Nothing of the running
    /// build is written to, so a failure leaves it as it is.
    /// </summary>
    /// <exception cref="SelfUpdateFailedException">The update stopped, and the message says where.</exception>
    Task<StagedSelfUpdate> StageAsync(BoreaRelease release, IProgress<SelfUpdateProgress>? progress = null, CancellationToken cancellationToken = default);
}
