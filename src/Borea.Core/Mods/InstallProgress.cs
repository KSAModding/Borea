namespace Borea.Core.Mods;

/// <summary>
/// The steps an install goes through, in order. Checking the archive against
/// its hash is part of <see cref="Downloading"/>, because the bytes are hashed
/// while they arrive.
/// </summary>
public enum InstallPhase
{
    /// <summary>The archive is being fetched. <see cref="InstallProgress.Download"/> carries the bytes.</summary>
    Downloading,

    /// <summary>The archive is being unpacked into a staging folder.</summary>
    Extracting,

    /// <summary>A loader's configuration file is being written.</summary>
    Configuring,

    /// <summary>The files are being moved into place and the install recorded.</summary>
    Finishing,
}

/// <summary>
/// Where one install stands, for a progress display.
/// </summary>
/// <param name="ModId">The content being installed.</param>
/// <param name="Version">The release being installed.</param>
/// <param name="Phase">The step the install is in.</param>
/// <param name="Download">The bytes so far while <see cref="InstallPhase.Downloading"/>, otherwise null.</param>
/// <param name="Step">Which operation of a plan this is, counting from 1. 1 outside a plan.</param>
/// <param name="StepCount">How many operations the plan runs. 1 outside a plan.</param>
public readonly record struct InstallProgress(
    string ModId,
    ModVersion Version,
    InstallPhase Phase,
    DownloadProgress? Download = null,
    int Step = 1,
    int StepCount = 1)
{
    public static InstallProgress Of(ModVersionMetadata release, InstallPhase phase, DownloadProgress? download = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new InstallProgress(release.ModId, release.Version, phase, download);
    }
}

/// <summary>
/// An <see cref="IProgress{T}"/> that reports on the calling thread, in order.
/// <see cref="Progress{T}"/> posts to the synchronization context, so a relay
/// built on it could hand a later report on before an earlier one.
/// </summary>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    public SynchronousProgress(Action<T> report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public void Report(T value) => _report(value);
}

public static class InstallProgressReports
{
    /// <summary>
    /// The byte reports of a download, turned into <see cref="InstallPhase.Downloading"/>
    /// reports for <paramref name="release"/>. Null when nobody listens.
    /// </summary>
    public static IProgress<DownloadProgress>? ForDownload(this IProgress<InstallProgress>? progress, ModVersionMetadata release) =>
        progress is null
            ? null
            : new SynchronousProgress<DownloadProgress>(bytes => progress.Report(InstallProgress.Of(release, InstallPhase.Downloading, bytes)));

    /// <summary>The reports of one operation, with its step and step count set.</summary>
    public static IProgress<InstallProgress>? ForStep(this IProgress<InstallProgress>? progress, int step, int stepCount) =>
        progress is null
            ? null
            : new SynchronousProgress<InstallProgress>(value => progress.Report(value with { Step = step, StepCount = stepCount }));

    public static void Report(this IProgress<InstallProgress>? progress, ModVersionMetadata release, InstallPhase phase) =>
        progress?.Report(InstallProgress.Of(release, phase));
}
