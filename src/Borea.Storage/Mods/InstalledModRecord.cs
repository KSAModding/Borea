using Borea.Core.Mods;

namespace Borea.Storage.Mods;

/// <summary>
/// Whether an installed record still holds the facts that an earlier read saw, so a replacement or a handover stops when another change reached the record first.
/// </summary>
internal static class InstalledModRecord
{
    public static bool Matches(InstalledMod current, InstalledMod expected) =>
        current.Version == expected.Version &&
        current.Reason == expected.Reason &&
        current.InstalledAt == expected.InstalledAt &&
        string.Equals(current.Checksum, expected.Checksum, StringComparison.OrdinalIgnoreCase) &&
        current.Ownership == expected.Ownership &&
        current.Storage == expected.Storage &&
        string.Equals(current.OwnershipToken, expected.OwnershipToken, StringComparison.Ordinal);
}
