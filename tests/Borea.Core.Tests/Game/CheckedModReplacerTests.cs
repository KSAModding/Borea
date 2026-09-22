using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Tests.Game;

public sealed class CheckedModReplacerTests
{
    private static readonly Guid Instance = Guid.NewGuid();

    [Theory]
    [MemberData(nameof(Replacements))]
    public async Task Replacements_RefuseOnABrokenShape(Func<IModReplacer, Task> replace)
    {
        var inner = new RecordingModReplacer();
        var replacer = new CheckedModReplacer(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Broken));

        var exception = await Assert.ThrowsAsync<GameShapeException>(() => replace(replacer));

        Assert.False(inner.Ran);
        Assert.Equal(GameShapeStatus.Broken, exception.Shape.Status);
        Assert.Contains("KSA", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Replacements))]
    public async Task Replacements_RunOnAShapeThatHolds(Func<IModReplacer, Task> replace)
    {
        var inner = new RecordingModReplacer();
        var replacer = new CheckedModReplacer(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Holds));

        await replace(replacer);

        Assert.True(inner.Ran);
    }

    public static TheoryData<Func<IModReplacer, Task>> Replacements() => new()
    {
        replacer => replacer.ReplaceAsync(Instance, null!, null!),
        replacer => replacer.ReplaceGuardedAsync(Instance, null!, null!, new InstallPlanningState("state")),
    };

    private sealed class RecordingModReplacer : IModReplacer
    {
        public bool Ran { get; private set; }

        public Task<ModReplacementResult> ReplaceAsync(
            Guid instanceId,
            InstalledMod expectedCurrent,
            ModVersionMetadata replacement,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Run<ModReplacementResult>();

        public Task<GuardedModReplacementResult> ReplaceGuardedAsync(
            Guid instanceId,
            InstalledMod expectedCurrent,
            ModVersionMetadata replacement,
            InstallPlanningState expectedState,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Run<GuardedModReplacementResult>();

        private Task<T> Run<T>()
        {
            Ran = true;
            return Task.FromResult<T>(default!);
        }
    }
}
