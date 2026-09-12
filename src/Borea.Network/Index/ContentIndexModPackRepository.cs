using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Tags;

namespace Borea.Network.Index;

/// <summary>Serves mod packs from the snapshot shared with the mod repository.</summary>
public sealed class ContentIndexModPackRepository : IModPackRepository
{
    private readonly IContentIndexSnapshotProvider _snapshots;

    public ContentIndexModPackRepository(IContentIndexSnapshotProvider snapshots)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
    }

    public async Task<IReadOnlyList<ModPackResult>> GetAvailableModPacksAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Packs
            .Where(pack => pack.IndexStatus?.State != IndexStatusState.Delisted)
            .Select(pack => LatestResult(snapshot, pack))
            .Where(result => result.Metadata is not null)
            .OrderBy(result => result.Id, ModIds.Comparer)
            .ToArray();
    }

    public async Task<ModPackResult?> GetAsync(string modPackId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var pack = snapshot.Packs.FirstOrDefault(candidate => ModIds.Equals(candidate.Id, modPackId));
        if (pack is not null)
        {
            if (pack.IndexStatus?.State == IndexStatusState.Delisted)
                return Result(snapshot, pack.Id, null, null, pack.IndexStatus, null);

            return LatestResult(snapshot, pack);
        }

        return PackDiagnosticIdentity(snapshot, modPackId);
    }

    public async Task<ModPackResult?> GetLatestAsync(string modPackId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var pack = AvailablePack(snapshot, modPackId);
        if (pack is null)
            return null;

        var latest = pack.Versions
            .Where(IsAvailable)
            .OrderByDescending(version => version.Metadata.Version)
            .FirstOrDefault();
        return latest is null ? null : Result(snapshot, pack, latest);
    }

    public async Task<ModPackResult?> GetVersionAsync(string modPackId, ModVersion version, CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var pack = snapshot.Packs.FirstOrDefault(candidate => ModIds.Equals(candidate.Id, modPackId));
        if (pack is null)
            return VersionDiagnosticIdentity(snapshot, modPackId, version);

        if (pack.IndexStatus?.State == IndexStatusState.Delisted)
            return Result(snapshot, pack.Id, version.ToString(), null, pack.IndexStatus, null);

        var match = pack.Versions.FirstOrDefault(candidate => candidate.Metadata.Version.Equals(version));
        return match is null
            ? VersionDiagnosticIdentity(snapshot, pack.Id, version)
            : Result(snapshot, pack, match);
    }

    public async Task<IReadOnlyList<ModPackResult>> GetAvailableVersionsAsync(string modPackId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var pack = AvailablePack(snapshot, modPackId);
        return pack?.Versions
            .Where(IsAvailable)
            .OrderByDescending(version => version.Metadata.Version)
            .Select(version => Result(snapshot, pack, version))
            .ToArray()
            ?? Array.Empty<ModPackResult>();
    }

    public async Task<IReadOnlyList<ModPackResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var packs = await GetAvailableModPacksAsync(cancellationToken).ConfigureAwait(false);
        return packs.Where(result => ContentTagFilter.MatchesSearch(result.Metadata!, query)).ToArray();
    }

    private static ContentIndexPack? AvailablePack(ContentIndexSnapshot snapshot, string id) =>
        snapshot.Packs.FirstOrDefault(pack =>
            ModIds.Equals(pack.Id, id)
            && pack.IndexStatus?.State != IndexStatusState.Delisted);

    private static bool IsAvailable(ContentIndexPackVersion version) =>
        version.IndexStatus?.State != IndexStatusState.Retracted;

    private static ModPackResult LatestResult(ContentIndexSnapshot snapshot, ContentIndexPack pack)
    {
        var latest = pack.Versions
            .Where(IsAvailable)
            .OrderByDescending(version => version.Metadata.Version)
            .FirstOrDefault();
        return latest is null
            ? Result(snapshot, pack.Id, null, null, pack.IndexStatus, null)
            : Result(snapshot, pack, latest);
    }

    private static ModPackResult Result(ContentIndexSnapshot snapshot, ContentIndexPack pack, ContentIndexPackVersion version) =>
        Result(snapshot, pack.Id, version.Metadata.Version.ToString(), version.Metadata, pack.IndexStatus, version.IndexStatus);

    private static ModPackResult Result(
        ContentIndexSnapshot snapshot,
        string id,
        string? version,
        ModPackMetadata? metadata,
        IndexStatus? packStatus,
        IndexStatus? versionStatus) =>
        new(id, version, metadata, packStatus, versionStatus, Diagnostics(snapshot, id, version));

    private static ModPackResult? PackDiagnosticIdentity(ContentIndexSnapshot snapshot, string id)
    {
        var identity = snapshot.Diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Scope == ContentIndexDiagnosticScope.Pack
            && ModIds.Equals(diagnostic.Id, id)
            && ModIds.IsValid(diagnostic.Id));
        return identity is null
            ? null
            : new ModPackResult(identity.Id!, null, null, null, null, Diagnostics(snapshot, identity.Id!, null));
    }

    private static ModPackResult? VersionDiagnosticIdentity(ContentIndexSnapshot snapshot, string id, ModVersion version)
    {
        var versionText = version.ToString();
        var identity = snapshot.Diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Scope == ContentIndexDiagnosticScope.PackVersion
            && ModIds.Equals(diagnostic.Id, id)
            && ModIds.IsValid(diagnostic.Id)
            && diagnostic.Version is not null
            && VersionEquals(diagnostic.Version, versionText));
        return identity is null
            ? null
            : new ModPackResult(identity.Id!, identity.Version, null, null, null, Diagnostics(snapshot, identity.Id!, identity.Version));
    }

    private static IReadOnlyList<ContentIndexDiagnostic> Diagnostics(ContentIndexSnapshot snapshot, string id, string? version) =>
        snapshot.Diagnostics
            .Where(diagnostic => ModIds.Equals(diagnostic.Id, id))
            .Where(diagnostic => diagnostic.Scope is ContentIndexDiagnosticScope.Pack
                or ContentIndexDiagnosticScope.PackVersion
                or ContentIndexDiagnosticScope.IndexStatus)
            .Where(diagnostic => diagnostic.Version is null || version is null || VersionEquals(diagnostic.Version, version))
            .ToArray();

    private static bool VersionEquals(string left, string right) =>
        ModVersion.TryParse(left, out var leftVersion)
        && ModVersion.TryParse(right, out var rightVersion)
        && leftVersion.Equals(rightVersion);
}
