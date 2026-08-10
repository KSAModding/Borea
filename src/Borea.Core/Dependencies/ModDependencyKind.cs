namespace Borea.Core.Dependencies;

/// <summary>
/// The kind of dependency, serialized lowercase in the metadata format.
/// </summary>
public enum ModDependencyKind
{
    /// <summary>
    /// The content does not function without it. Installed always.
    /// </summary>
    Required = 0,

    /// <summary>
    /// Used when present, not installed by default.
    /// </summary>
    Optional = 1,

    /// <summary>
    /// Selected by default, deselectable by the user.
    /// </summary>
    Recommends = 2,

    /// <summary>
    /// Listed, not selected.
    /// </summary>
    Suggests = 3,

    /// <summary>
    /// Must not be installed together. Bounds narrow the conflicting range; no bounds means every version conflicts.
    /// </summary>
    Conflict = 4,

    /// <summary>
    /// A value this client version does not know.
    /// </summary>
    Unknown = 5,
}
