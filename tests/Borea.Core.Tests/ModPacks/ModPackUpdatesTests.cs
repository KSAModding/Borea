using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Tests.ModPacks;

public sealed class ModPackUpdatesTests
{
    [Fact]
    public void Compare_ListsAddedRepinnedAndDroppedMods_AndLeavesThePlayersModsOut()
    {
        var instance = Instance(
            Installed("Same", "1.0.0", InstallReason.ModPack),
            Installed("Repinned", "1.0.0", InstallReason.ModPack),
            Installed("Dropped", "1.0.0", InstallReason.ModPack),
            Installed("Foreign", "1.0.0", InstallReason.ModPack, ModInstallOwnership.Foreign),
            Installed("Player", "1.0.0", InstallReason.Manual),
            Installed("Helper", "1.0.0", InstallReason.Dependency));

        var changes = ModPackUpdates.Compare(instance, Pack("2.0.0", ("Same", "1.0.0"), ("Repinned", "1.1.0"), ("Added", "1.0.0")));

        Assert.Equal(
        [
            new ModPackChange("Added", ModPackChangeKind.Add, null, ModVersion.Parse("1.0.0")),
            new ModPackChange("Repinned", ModPackChangeKind.Change, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.1.0")),
            new ModPackChange("Dropped", ModPackChangeKind.Remove, ModVersion.Parse("1.0.0"), null),
            new ModPackChange("Foreign", ModPackChangeKind.Keep, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.0.0")),
        ], changes);
    }

    [Fact]
    public void KeepNeeded_DroppedModThatAStayingModRequires_IsKept()
    {
        var instance = Instance(
            Installed("Needed", "1.0.0", InstallReason.ModPack),
            Installed("Unneeded", "1.0.0", InstallReason.ModPack),
            Installed("Player", "1.0.0", InstallReason.Manual, dependencies: [Requires("Needed")]));
        var changes = ModPackUpdates.Compare(instance, Pack("2.0.0", ("Other", "1.0.0")));

        var decided = ModPackUpdates.KeepNeeded(instance, changes, [Release("Other", "1.0.0")]);

        Assert.Equal(ModPackChangeKind.Keep, decided.Single(change => change.ModId == "Needed").Kind);
        Assert.Equal(ModPackChangeKind.Remove, decided.Single(change => change.ModId == "Unneeded").Kind);
    }

    [Fact]
    public void KeepNeeded_DroppedModThatANewPinRequires_IsKept()
    {
        var instance = Instance(Installed("Needed", "1.0.0", InstallReason.ModPack));
        var changes = ModPackUpdates.Compare(instance, Pack("2.0.0", ("Other", "1.0.0")));

        var decided = ModPackUpdates.KeepNeeded(instance, changes, [Release("Other", "1.0.0", [Requires("Needed")])]);

        Assert.Equal(ModPackChangeKind.Keep, Assert.Single(decided, change => change.ModId == "Needed").Kind);
    }

    [Fact]
    public void KeepNeeded_DroppedChainThatNothingElseNeeds_IsRemoved_AndOneOfTwoAlternativesStays()
    {
        var instance = Instance(
            Installed("Top", "1.0.0", InstallReason.ModPack, dependencies: [Requires("Bottom")]),
            Installed("Bottom", "1.0.0", InstallReason.ModPack),
            Installed("First", "1.0.0", InstallReason.ModPack),
            Installed("Second", "1.0.0", InstallReason.ModPack),
            Installed("Player", "1.0.0", InstallReason.Manual, dependencies: [ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("First"), new ModDependencyAlternative("Second")])]));
        var changes = ModPackUpdates.Compare(instance, Pack("2.0.0", ("Other", "1.0.0")));

        var decided = ModPackUpdates.KeepNeeded(instance, changes, []);

        Assert.Equal(ModPackChangeKind.Remove, decided.Single(change => change.ModId == "Top").Kind);
        Assert.Equal(ModPackChangeKind.Remove, decided.Single(change => change.ModId == "Bottom").Kind);
        Assert.Single(decided, change => change.ModId is "First" or "Second" && change.Kind == ModPackChangeKind.Keep);
        Assert.Single(decided, change => change.ModId is "First" or "Second" && change.Kind == ModPackChangeKind.Remove);
    }

    [Fact]
    public void Draft_LeavesOutRemovedMods_AndKeepsTheReasonsOfTheOthers()
    {
        var instance = Instance(Installed("Removed", "1.0.0", InstallReason.ModPack), Installed("Kept", "1.0.0", InstallReason.ModPack, ModInstallOwnership.Foreign));

        var draft = ModPackUpdates.Draft(instance, ModPackUpdates.Compare(instance, Pack("2.0.0", ("Other", "1.0.0"))));

        var kept = Assert.Single(draft.Mods);
        Assert.Equal("Kept", kept.ModId);
        Assert.Equal(InstallReason.ModPack, kept.Reason);
    }

    [Fact]
    public async Task FindNewer_IgnoresARetractedAndAnOlderVersion()
    {
        var source = new InstanceSource.FromModPack("Pack", ModVersion.Parse("1.0.0"));
        var retracted = new IndexStatus(IndexStatusState.Retracted, "retracted", reason: "Broken.");

        Assert.Null(await ModPackUpdates.FindNewerAsync(new FakePackRepository(Result(Pack("1.1.0", ("Mod", "1.0.0")), retracted)), source));
        Assert.Null(await ModPackUpdates.FindNewerAsync(new FakePackRepository(Result(Pack("1.0.0", ("Mod", "1.0.0")), null)), source));
        Assert.Null(await ModPackUpdates.FindNewerAsync(new FakePackRepository(Result(Pack("1.1.0", ("Mod", "1.0.0")), null)), InstanceSource.Custom.Value));
        Assert.Equal("1.1.0", (await ModPackUpdates.FindNewerAsync(new FakePackRepository(Result(Pack("1.1.0", ("Mod", "1.0.0")), null)), source))?.Version);
    }

    private static Instance Instance(params InstalledMod[] mods)
        => Borea.Core.Instances.Instance.FromExisting(Guid.NewGuid(), "Target", new InstanceSource.FromModPack("Pack", ModVersion.Parse("1.0.0")), DateTimeOffset.UnixEpoch, mods, false);

    private static InstalledMod Installed(string id, string version, InstallReason reason, ModInstallOwnership ownership = ModInstallOwnership.Borea, IReadOnlyList<ModDependency>? dependencies = null)
        => new(id, ModVersion.Parse(version), reason, DateTimeOffset.UnixEpoch, Release(id, version, dependencies), ownership: ownership, ownershipToken: ownership == ModInstallOwnership.Borea ? "token" : null);

    private static ModDependency Requires(string id) => new(id, ModDependencyKind.Required);

    private static ModVersionMetadata Release(string id, string version, IReadOnlyList<ModDependency>? dependencies = null)
        => new(1, id, ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, new DownloadInfo($"https://example.com/{id}.zip", new string('A', 64), 1, "application/zip"), 1, dependencies ?? []);

    private static ModPackMetadata Pack(string version, params (string Id, string Version)[] pins)
        => new(1, "Pack", "test", "Pack", ["Author"], "Pack.", "CC0-1.0", new Dictionary<string, string> { ["forums"] = "https://example.com/pack" }, "2026.7", ModVersion.Parse(version), DateTimeOffset.UnixEpoch, pins.Select(pin => new ModPackEntry(pin.Id, ModVersion.Parse(pin.Version))).ToList());

    private static ModPackResult Result(ModPackMetadata metadata, IndexStatus? status)
        => new(metadata.ModPackId, metadata.Version.ToString(), metadata, null, status, []);

    private sealed class FakePackRepository(ModPackResult latest) : IModPackRepository
    {
        public Task<IReadOnlyList<ModPackResult>> GetAvailableModPacksAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModPackResult>>([latest]);
        public Task<ModPackResult?> GetAsync(string modPackId, CancellationToken cancellationToken = default) => Task.FromResult<ModPackResult?>(latest);
        public Task<ModPackResult?> GetLatestAsync(string modPackId, CancellationToken cancellationToken = default) => Task.FromResult<ModPackResult?>(latest);
        public Task<ModPackResult?> GetVersionAsync(string modPackId, ModVersion version, CancellationToken cancellationToken = default) => Task.FromResult<ModPackResult?>(latest);
        public Task<IReadOnlyList<ModPackResult>> GetAvailableVersionsAsync(string modPackId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModPackResult>>([latest]);
        public Task<IReadOnlyList<ModPackResult>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModPackResult>>([latest]);
    }
}
