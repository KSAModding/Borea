namespace Borea.Core.Updates;

/// <summary>
/// A self-update that stopped. Borea keeps a build that works, unless the reason is
/// <see cref="SelfUpdateFailure.Restore"/>, which names the file a user has to rename back.
/// </summary>
public sealed class SelfUpdateFailedException : InvalidOperationException
{
    public SelfUpdateFailure Reason { get; }

    public SelfUpdateFailedException(SelfUpdateFailure reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }
}
