namespace Borea.Core.Dependencies;

public enum DependencyOutcome
{
    /// <summary>The instance meets the entry.</summary>
    Satisfied = 0,

    /// <summary>Required and missing.</summary>
    Install = 1,

    /// <summary>Recommended and missing. Preselected, deselectable.</summary>
    SelectByDefault = 2,

    /// <summary>Optional or suggested and missing. Listed, not selected.</summary>
    Offer = 3,

    /// <summary>An installed version falls inside a conflicting range.</summary>
    Conflict = 4,

    Unknown = 5,
}
