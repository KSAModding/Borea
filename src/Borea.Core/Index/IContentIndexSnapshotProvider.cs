namespace Borea.Core.Index;

/// <summary>Provides one shared, refreshed content index snapshot to index repositories.</summary>
public interface IContentIndexSnapshotProvider
{
    Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
