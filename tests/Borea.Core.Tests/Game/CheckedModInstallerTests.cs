using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Tests.Game;

public sealed class CheckedModInstallerTests
{
    private static readonly Guid Instance = Guid.NewGuid();

    [Theory]
    [MemberData(nameof(Installs))]
    public async Task Installs_RefuseOnABrokenShape(Func<IModInstaller, Task> install)
    {
        var inner = new RecordingModInstaller();
        var installer = new CheckedModInstaller(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Broken));

        var exception = await Assert.ThrowsAsync<GameShapeException>(() => install(installer));

        Assert.False(inner.Ran);
        Assert.Equal(GameShapeStatus.Broken, exception.Shape.Status);
        Assert.Contains("KSA", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Installs))]
    public async Task Installs_RunOnAShapeThatHolds(Func<IModInstaller, Task> install)
    {
        var inner = new RecordingModInstaller();
        var installer = new CheckedModInstaller(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Holds));

        await install(installer);

        Assert.True(inner.Ran);
    }

    public static TheoryData<Func<IModInstaller, Task>> Installs() => new()
    {
        installer => installer.InstallAsync(Instance, null!, InstallReason.Manual, enable: true),
        installer => installer.InstallGuardedAsync(Instance, null!, InstallReason.Manual, enable: true, new InstallPlanningState("state")),
    };

    private sealed class RecordingModInstaller : IModInstaller
    {
        public bool Ran { get; private set; }

        public Task<InstallResult> InstallAsync(
            Guid instanceId,
            ModVersionMetadata release,
            InstallReason reason,
            bool enable,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Run<InstallResult>();

        public Task<GuardedInstallResult> InstallGuardedAsync(
            Guid instanceId,
            ModVersionMetadata release,
            InstallReason reason,
            bool enable,
            InstallPlanningState expectedState,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Run<GuardedInstallResult>();

        private Task<T> Run<T>()
        {
            Ran = true;
            return Task.FromResult<T>(default!);
        }
    }
}
