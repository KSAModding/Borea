using Borea.Core.Mods;

namespace Borea.Cli.Tests;

internal sealed class FakeArchiveReleaseLookup : IModArchiveReleaseLookup
{
    public List<ModVersionMetadata> Releases { get; } = new();

    public Task<ModVersionMetadata?> FindBySha256Async(string modId, string sha256, CancellationToken cancellationToken = default)
        => Task.FromResult(Releases.FirstOrDefault(release =>
            ModIds.Equals(release.ModId, modId)
            && string.Equals(release.Download.Sha256, sha256, StringComparison.OrdinalIgnoreCase)));
}
