using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Tests.Planning;

public sealed class ModListUpdatesTests
{
    [Fact]
    public void Compare_ListsAddedChangedAndRemovedLinkMods_KeepsPlayerMods_AndLeavesDependencies()
    {
        var pinned = Installed("Pinned", "1.0.0", InstallReason.Manual);
        pinned.SetPinned(true);
        var instance = Borea.Core.Instances.Instance.FromExisting(
            Guid.NewGuid(),
            "Server",
            InstanceSource.Custom.Value,
            DateTimeOffset.UnixEpoch,
            [
                Installed("Same", "1.0.0", InstallReason.Manual),
                Installed("Changed", "1.0.0", InstallReason.Manual),
                Installed("Dropped", "1.0.0", InstallReason.Manual),
                pinned,
                Installed("Player", "1.0.0", InstallReason.Manual),
                Installed("Helper", "1.0.0", InstallReason.Dependency),
            ],
            false);
        instance.SetLinkedModList(new LinkedModList("srv", ["Same", "Changed", "Dropped", "Pinned"]));
        var target = new ModList("Server", [Entry("Same", "1.0.0"), Entry("Changed", "1.1.0"), Entry("Pinned", "1.1.0"), Entry("Added", "1.0.0")]);

        var changes = ModListUpdates.Compare(instance, target);

        Assert.Equal(
        [
            new ModPackChange("Added", ModPackChangeKind.Add, null, ModVersion.Parse("1.0.0")),
            new ModPackChange("Changed", ModPackChangeKind.Change, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.1.0")),
            new ModPackChange("Dropped", ModPackChangeKind.Remove, ModVersion.Parse("1.0.0"), null),
            new ModPackChange("Player", ModPackChangeKind.Keep, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.0.0")),
            new ModPackChange("Pinned", ModPackChangeKind.Pinned, ModVersion.Parse("1.0.0"), ModVersion.Parse("1.0.0")),
        ], changes);
    }

    private static ModListEntry Entry(string id, string version) => new(id, ModVersion.Parse(version), enabled: true);

    private static InstalledMod Installed(string id, string version, InstallReason reason)
        => new(id, ModVersion.Parse(version), reason, DateTimeOffset.UnixEpoch, Release(id, version), ownership: ModInstallOwnership.Borea, ownershipToken: "token");

    private static ModVersionMetadata Release(string id, string version)
        => new(1, id, ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, new DownloadInfo($"https://example.com/{id}.zip", new string('A', 64), 1, "application/zip"), 1, []);
}
