namespace Borea.Storage.Index;

/// <summary>
/// One index entry whose spec_version this build does not know. Identity
/// fields contain what was available before the current shape was read.
/// </summary>
public sealed record UnknownIndexVersionEntry(string? Id, string? Version, int SpecVersion, string Reason)
{
    public UnknownIndexVersionEntry(string? id, int specVersion)
        : this(id, null, specVersion, $"The entry uses unsupported spec_version {specVersion}.")
    {
    }
}
