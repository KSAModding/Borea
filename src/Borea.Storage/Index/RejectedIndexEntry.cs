namespace Borea.Storage.Index;

/// <summary>
/// One index entry that could not be read. Identity fields contain what this
/// build could recover from the raw JSON before it rejected the entry.
/// </summary>
public sealed record RejectedIndexEntry(string? Id, string? Version, string Reason)
{
    public RejectedIndexEntry(string? id, string reason)
        : this(id, null, reason)
    {
    }
}
