using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Tests.ModLoaders;

namespace Borea.Core.Tests.Planning;

public sealed class ModListInstallerTests
{
    [Fact]
    public async Task CheckedReleases_AreTheOnlyReleasesTheEntriesTake()
    {
        var checkedRelease = Release("index", 'A');
        var request = new ModListRequest(
            new ModList("Server", [new ModListEntry("StarMap", checkedRelease.Version, enabled: true), new ModListEntry("Other", checkedRelease.Version, enabled: true)]),
            InstanceSource.Custom.Value,
            new LoaderUpdateTests.FakeRepository(Release("spacedock", 'B')),
            Releases: [checkedRelease]);

        var items = await ModListInstaller.ReadItemsAsync(request, CancellationToken.None);

        Assert.Same(checkedRelease, items[0].Release);
        Assert.True(items[1].IsUnknown);
    }

    private static ModVersionMetadata Release(string source, char hash) => new(
        1,
        "StarMap",
        ModVersion.Parse("1.0.0"),
        ReleaseStatus.Stable,
        DateTimeOffset.UnixEpoch,
        "2026.8.3.5117",
        5117,
        new DownloadInfo("https://example.com/StarMap.zip", new string(hash, 64), 1, "application/zip"),
        1,
        [],
        source: source);
}
