namespace Borea.Storage.Index;

/// <summary>
/// One index entry (listing, release, pack, or pack version) that could not
/// be read. <see cref="Id"/> is whatever this build could recover before
/// giving up, extracted straight from the raw JSON so a bad document still
/// names itself.
/// </summary>
public sealed record RejectedIndexEntry(string? Id, string Reason);
