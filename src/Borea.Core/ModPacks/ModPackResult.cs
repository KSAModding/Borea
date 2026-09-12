using System.Collections.ObjectModel;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

/// <summary>One pack query result, including index state that can make its metadata unusable.</summary>
public sealed class ModPackResult
{
    public string Id { get; }
    public string? Version { get; }
    public ModPackMetadata? Metadata { get; }
    public IndexStatus? PackStatus { get; }
    public IndexStatus? VersionStatus { get; }
    public IReadOnlyList<ContentIndexDiagnostic> Diagnostics { get; }

    public ModPackResult(
        string id,
        string? version,
        ModPackMetadata? metadata,
        IndexStatus? packStatus,
        IndexStatus? versionStatus,
        IReadOnlyList<ContentIndexDiagnostic> diagnostics)
    {
        ModIds.Validate(id, nameof(id));
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (metadata is not null && !ModIds.Equals(id, metadata.ModPackId))
            throw new ArgumentException("The metadata id must match the result id.", nameof(metadata));

        if (metadata is not null && !string.Equals(version, metadata.Version.ToString(), StringComparison.Ordinal))
            throw new ArgumentException("The metadata version must match the result version.", nameof(metadata));

        Id = id;
        Version = version;
        Metadata = metadata;
        PackStatus = packStatus;
        VersionStatus = versionStatus;
        Diagnostics = new ReadOnlyCollection<ContentIndexDiagnostic>(diagnostics.ToArray());
    }
}
