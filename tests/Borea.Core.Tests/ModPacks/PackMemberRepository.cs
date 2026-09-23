using Borea.Core.Dependencies;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Tests.ModPacks;

/// <summary>Like the index repository: the listings and their releases, with a yanked release skipped except for an exact read.</summary>
internal sealed class PackMemberRepository(IReadOnlyList<ModMetadata> listings, IReadOnlyList<ModVersionMetadata> releases) : IModRepository
{
    public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(listings);

    public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default)
        => Task.FromResult(listings.FirstOrDefault(listing => ModIds.Equals(listing.ModId, modId)));

    public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default)
        => Task.FromResult(Of(modId).Where(release => !release.Yanked).OrderByDescending(release => release.Version).FirstOrDefault());

    public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
        => Task.FromResult(Of(modId).FirstOrDefault(release => release.Version == version));

    public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ModVersion>>(Of(modId).Where(release => !release.Yanked).Select(release => release.Version).OrderByDescending(version => version).ToList());

    public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ModMetadata>>([]);

    private IEnumerable<ModVersionMetadata> Of(string modId) => releases.Where(release => ModIds.Equals(release.ModId, modId));

    public static ModMetadata Listing(string id, string name) => new(
        specVersion: 1,
        modId: id,
        source: "index",
        name: name,
        authors: ["Maxi", "cairn5"],
        abstractText: name + " abstract.",
        license: "MIT",
        links: new Dictionary<string, string> { ["forums"] = $"https://forums.example/{id}" },
        gameMin: "2026.7.4.2131");

    public static ModVersionMetadata Release(string id, string version, ReleaseStatus status = ReleaseStatus.Stable, bool yanked = false) => new(
        1,
        id,
        ModVersion.Parse(version),
        status,
        DateTimeOffset.UnixEpoch,
        "2026.7.4.2131",
        2131,
        new DownloadInfo($"https://example.com/{id}/{version}.zip", new string('A', 64), 1, "application/zip"),
        1,
        Array.Empty<ModDependency>(),
        yanked: yanked);

    public static ModPackMetadata Pack(params (string Id, string Version)[] pins)
        => new(1, "Pack", "index", "Pack", ["Maxi"], "Pack.", "CC0-1.0", new Dictionary<string, string> { ["forums"] = "https://forums.example/pack" }, "2026.7", ModVersion.Parse("1.0.0"), DateTimeOffset.UnixEpoch, pins.Select(pin => new ModPackEntry(pin.Id, ModVersion.Parse(pin.Version))).ToList());
}
