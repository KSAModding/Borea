namespace Borea.Core.Planning;

/// <summary>The instance is no longer in the state that its plan saw, so the executor refuses the plan.</summary>
public sealed class PlannedInstanceChangedException : InvalidOperationException
{
    public PlannedInstanceChangedException(string message)
        : base(message)
    {
    }
}
