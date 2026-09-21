namespace Borea.Core.Planning;

/// <summary>
/// Compares what an install plan writes with the free space where it is
/// written.
/// </summary>
public interface IInstallSpaceCheck
{
    /// <summary>
    /// Refuses a plan that does not fit. A release that states neither its
    /// archive size nor its unpacked size is left out of the estimate instead
    /// of blocking the install, and a volume that cannot be read is not
    /// checked.
    /// </summary>
    /// <exception cref="InsufficientDiskSpaceException">The plan needs more room than a volume has.</exception>
    void EnsureFits(InstallPlan plan);
}
