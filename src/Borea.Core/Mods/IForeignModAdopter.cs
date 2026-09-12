namespace Borea.Core.Mods;

public interface IForeignModAdopter
{
    Task<IReadOnlyList<ForeignMod>> ScanAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task<ForeignModAdoptionResult> AdoptArchiveAsync(
        Guid instanceId,
        string folderName,
        string archivePath,
        CancellationToken cancellationToken = default);
}
public sealed record ForeignModAdoptionResult(string Sha256, ForeignMod? ForeignMod, InstalledMod? InstalledMod)
{
    public bool Matched => InstalledMod is not null;
}
