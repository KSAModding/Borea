using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Cli.Tests;

/// <summary>
/// Records every install request and its progress, and writes nothing. Without <see cref="Result"/> it
/// gives the results <see cref="Borea.Storage.ModPacks.ModPackInstaller"/> gives before it plans:
/// a retracted pack version without the caller's choice leaves every pin unresolved, and
/// an unlisted pin or a yanked pin without the caller's choice is unresolved while every
/// other pin is not attempted. When every pin can go ahead, every pin reports installed.
/// </summary>
internal sealed class FakeModPackInstaller : IModPackInstaller
{
    public List<ModPackInstallRequest> Requests { get; } = new();

    public List<IProgress<InstallProgress>?> Progress { get; } = new();

    public Func<ModPackInstallRequest, ModPackInstallResult>? Result { get; set; }

    public async Task<ModPackInstallResult> InstallAsync(ModPackInstallRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        Progress.Add(progress);
        if (Result is not null)
            return Result(request);

        var pack = request.Pack.Metadata!;
        if (request.Pack.VersionStatus?.State == IndexStatusState.Retracted && !request.ProceedWithRetractedPack)
        {
            return new ModPackInstallResult(
                request.InstanceId,
                null,
                pack.Mods.Select(pin => Member(pin, ModPackMemberStatus.Unresolved, "Caller confirmation is required for the retracted pack version.")).ToArray(),
                new[] { new PlanningMessage(pack.ModPackId, PlanningMessageKind.RetractedPack) { Value = request.Pack.VersionStatus.Reason } },
                false);
        }

        var members = new List<ModPackMemberResult>();
        var warnings = new List<PlanningMessage>();
        foreach (var pin in pack.Mods)
        {
            var release = await request.Repository.GetReleaseAsync(pin.ContentId, pin.Version, cancellationToken);
            if (release is null)
            {
                members.Add(Member(pin, ModPackMemberStatus.Unresolved, "The exact pinned release is not listed."));
                warnings.Add(new PlanningMessage(pin.ContentId, PlanningMessageKind.UnlistedPin));
                continue;
            }

            var proceeds = request.ProceedWithYankedMembers?.Any(id => ModIds.Equals(id, pin.ContentId)) ?? false;
            if (release.Yanked && !proceeds)
            {
                members.Add(Member(pin, ModPackMemberStatus.Unresolved, "Caller confirmation is required for the yanked release."));
                warnings.Add(new PlanningMessage(pin.ContentId, PlanningMessageKind.YankedPin) { Value = release.YankedReason });
            }
        }

        if (members.Count > 0)
        {
            foreach (var pin in pack.Mods.Where(pin => members.All(member => !ModIds.Equals(member.ModId, pin.ContentId))))
                members.Add(Member(pin, ModPackMemberStatus.NotAttempted, "Another pack member needs caller action."));
            return new ModPackInstallResult(request.InstanceId, null, members, warnings, false);
        }

        return new ModPackInstallResult(
            request.InstanceId,
            null,
            pack.Mods.Select(pin => Member(pin, ModPackMemberStatus.Installed)).ToArray(),
            warnings,
            true);
    }

    public Task<ModPackInstallResult> PlanAsync(ModPackInstallRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A test that plans a pack uses the real pack installer.");

    public Task<ModPackInstallResult> PlanNewAsync(string instanceName, ModPackInstallRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A test that plans a pack uses the real pack installer.");

    public Task<ModPackInstallResult> CreateAndInstallAsync(string instanceName, ModPackInstallRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A test that creates an instance from a pack uses the real pack installer.");

    public static ModPackMemberResult Member(ModPackEntry pin, ModPackMemberStatus status, string? message = null) =>
        new(pin.ContentId, pin.Version, InstallReason.ModPack, status, message);
}
