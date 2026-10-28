namespace Borea.Core.Stewardship;

/// <summary>Why a change of index-status.toml is refused before anything is written, as tools/check_status.py would refuse it.</summary>
public enum IndexStatusRefusal
{
    /// <summary>The reason is empty, or not one line.</summary>
    InvalidReason,

    /// <summary>A retracted state names no pack version.</summary>
    MissingVersion,

    /// <summary>The id already has a state, or this pack version is already retracted.</summary>
    Duplicate,

    /// <summary>The id is neither under listings/ nor under packs/ of the base branch.</summary>
    UnknownId,

    /// <summary>Only a pack version is retracted, and the id is no pack.</summary>
    NotAPack,

    /// <summary>The pack has no such version on the base branch.</summary>
    UnknownVersion,

    /// <summary>The state to lift is no longer in the file.</summary>
    NotInFile,

    /// <summary>No version of the pack pins the mod, or each one that does is retracted already.</summary>
    NotPinned,

    /// <summary>Neither the ownership proof nor the steward confirms that the reporter owns the listing of the mod.</summary>
    NotConfirmed,
}

public sealed class IndexStatusRefusedException(IndexStatusRefusal refusal, string id, string? version = null)
    : Exception($"{refusal}: {id}{(version is null ? string.Empty : " " + version)}")
{
    public IndexStatusRefusal Refusal { get; } = refusal;

    public string Id { get; } = id;

    public string? Version { get; } = version;
}
