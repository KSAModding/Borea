namespace Borea.Core.Index;

public enum ContentIndexDiagnosticKind
{
    Malformed = 0,
    UnsupportedVersion = 1,
    UnsupportedValue = 2,
}

public enum ContentIndexDiagnosticScope
{
    Listing = 0,
    Release = 1,
    Pack = 2,
    PackVersion = 3,
    IndexStatus = 4,
    GameVersions = 5,
}

/// <summary>One part of the snapshot that Borea could not read safely.</summary>
public sealed record ContentIndexDiagnostic(
    ContentIndexDiagnosticKind Kind,
    ContentIndexDiagnosticScope Scope,
    string Reason,
    string? Id = null,
    string? Version = null,
    int? SpecVersion = null);
