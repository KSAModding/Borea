using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Cli.Tests;

/// <summary>
/// Installs without a download: the folder with its mod.toml and ownership file, the record, and the manifest entry.
/// </summary>
internal sealed class FolderInstaller(BoreaServices graph, string? failOn = null) : IModInstaller
{
    public Task<InstallResult> InstallAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public async Task<GuardedInstallResult> InstallGuardedAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (ModIds.Equals(release.ModId, failOn))
            throw new IOException("The disk is full.");

        var token = Guid.NewGuid().ToString("N");
        var folder = Directory.CreateDirectory(Path.Combine(graph.Paths.GetInstanceModsFolder(instanceId), release.ModId));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "mod.toml"), $"name = \"{release.ModId}\"", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, ".borea-owner"), token, cancellationToken);
        var installed = new InstalledMod(release.ModId, release.Version, reason, DateTimeOffset.UtcNow, release, release.Download.Sha256, ModInstallOwnership.Borea, token);
        var state = await graph.Instances.UpdateAsync(instanceId, current =>
        {
            if (!expectedState.Matches(current))
                throw new InvalidOperationException("The instance changed after Borea planned the operation.");

            current.AddMod(installed);
            return InstallPlanningState.Capture(current);
        }, cancellationToken);
        var entry = await graph.ModState.AddEntryAsync(instanceId, release.ModId, enable, cancellationToken);
        var download = new DownloadResult(release.Download.Url, release.Download.SizeBytes ?? 0, release.Download.Sha256 ?? string.Empty);
        return new GuardedInstallResult(new InstallResult(installed, download, entry), state);
    }
}
