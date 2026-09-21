using Borea.Core.Paths;
using Borea.Core.Planning;
using Borea.Storage.Files;

namespace Borea.Storage.Mods;

/// <summary>
/// <see cref="IInstallSpaceCheck"/> over the real volumes. The unpacked mods
/// stay in the library and each archive is downloaded into the temporary
/// folder, so the two counts meet only when both folders are on one volume.
/// </summary>
public sealed class DriveInstallSpaceCheck : IInstallSpaceCheck
{
    /// <summary>The share of the estimate that stays free on top of it.</summary>
    public const double MarginFraction = 0.1;

    /// <summary>The smallest margin, so that a small plan also leaves room behind.</summary>
    public const long MinimumMarginBytes = 50L * 1000 * 1000;

    private readonly IGamePathProvider _paths;
    private readonly IFreeSpaceProbe _freeSpace;
    private readonly string _temporaryFolder;

    public DriveInstallSpaceCheck(IGamePathProvider paths, IFreeSpaceProbe? freeSpace = null, string? temporaryFolder = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _freeSpace = freeSpace ?? new DriveFreeSpaceProbe();
        _temporaryFolder = temporaryFolder ?? Path.GetTempPath();
    }

    public void EnsureFits(InstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var unpacked = plan.Operations.Sum(operation => operation.Release.InstallSizeBytes ?? 0);

        // Every archive is deleted as soon as its mod is in place, so only the largest one is on the disk at a time.
        var archive = plan.Operations.Count == 0 ? 0 : plan.Operations.Max(operation => operation.Release.Download.SizeBytes ?? 0);

        var needs = new List<(FreeSpace Volume, long Bytes)>();
        if (unpacked > 0 && _freeSpace.Measure(_paths.GetInstancesRoot()) is { } library)
            needs.Add((library, unpacked));
        if (archive > 0 && _freeSpace.Measure(_temporaryFolder) is { } temporary)
            needs.Add((temporary, archive));

        foreach (var volume in needs.GroupBy(need => need.Volume.VolumeName, VolumeComparer))
        {
            var estimate = volume.Sum(need => need.Bytes);
            var required = estimate + Math.Max(MinimumMarginBytes, (long)(estimate * MarginFraction));
            var available = volume.Min(need => need.Volume.AvailableBytes);
            if (available < required)
                throw new InsufficientDiskSpaceException(volume.Key, required, available);
        }
    }

    private static StringComparer VolumeComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
