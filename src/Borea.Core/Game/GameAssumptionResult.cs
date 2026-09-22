namespace Borea.Core.Game;

/// <param name="Detail">What was found, for the log and the command line. Always set, also when the assumption holds.</param>
public sealed record GameAssumptionResult(GameAssumption Assumption, GameAssumptionState State, string Detail)
{
    public bool IsBroken => State == GameAssumptionState.Broken;
}
