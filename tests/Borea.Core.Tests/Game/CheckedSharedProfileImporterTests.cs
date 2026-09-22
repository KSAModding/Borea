using Borea.Core.Game;
using Borea.Core.Instances;

namespace Borea.Core.Tests.Game;

public sealed class CheckedSharedProfileImporterTests
{
    [Fact]
    public async Task ImportAsync_RefusesOnABrokenShape()
    {
        var inner = new RecordingSharedProfileImporter();
        var importer = new CheckedSharedProfileImporter(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Broken));

        var exception = await Assert.ThrowsAsync<GameShapeException>(() => importer.ImportAsync("New"));

        Assert.False(inner.Imported);
        Assert.Equal(GameShapeStatus.Broken, exception.Shape.Status);
    }

    [Fact]
    public async Task ImportAsync_RunsOnAShapeThatHolds()
    {
        var inner = new RecordingSharedProfileImporter();
        var importer = new CheckedSharedProfileImporter(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Holds));

        await importer.ImportAsync("New");

        Assert.True(inner.Imported);
    }

    /// <summary>The App lists the mods to offer the import, so a broken shape must not hide them.</summary>
    [Fact]
    public async Task GetModsAsync_PassesThroughOnABrokenShape()
    {
        var importer = new CheckedSharedProfileImporter(new RecordingSharedProfileImporter(), new FixedGameShapeCheck(FixedGameShapeCheck.Broken));

        Assert.Empty(await importer.GetModsAsync());
    }

    private sealed class RecordingSharedProfileImporter : ISharedProfileImporter
    {
        public bool Imported { get; private set; }

        public Task<IReadOnlyList<SharedProfileMod>> GetModsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SharedProfileMod>>([]);

        public Task<SharedProfileImportResult> ImportAsync(string instanceName, CancellationToken cancellationToken = default)
        {
            Imported = true;
            return Task.FromResult<SharedProfileImportResult>(default!);
        }
    }
}
