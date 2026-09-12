namespace Borea.Storage.Index;

/// <summary>
/// What became of one index entry (a listing, a release, a pack, or a pack
/// version) once its own error boundary ran.
/// </summary>
public enum ParseOutcomeKind
{
    /// <summary>Deserialized and within a spec version this build reads.</summary>
    Valid,

    /// <summary>Shape looks fine, but its spec_version is above what this build reads.</summary>
    Unknown,

    /// <summary>Broken: invalid JSON, a missing required field, or a value that failed its own rules.</summary>
    Malformed,
}
