using System.Text.Json.Serialization;

namespace Borea.Core.Index;

/// <summary>
/// The "index_status.state" property of a listing in index.json
/// </summary>
public enum IndexStatusState
{
    /// <summary>Unknown state, default</summary>
    Unknown = 0,
    /// <summary>The listing was taken down, now a tombstone</summary>
    Delisted = 1,

    /// <summary>Currently disputed, ships whole, only a warning</summary>
    Disputed = 2,

    /// <summary>Mod Pack ONLY, essentially yanked Mod Pack version</summary>
    Retracted = 3,
}
