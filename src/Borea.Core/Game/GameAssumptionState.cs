namespace Borea.Core.Game;

public enum GameAssumptionState
{
    /// <summary>The installation has the shape Borea expects.</summary>
    Holds,

    /// <summary>The installation is there and does not have that shape.</summary>
    Broken,

    /// <summary>Nothing was there to check, so the assumption says nothing either way.</summary>
    NotChecked,
}
