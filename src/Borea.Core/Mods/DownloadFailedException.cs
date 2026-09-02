namespace Borea.Core.Mods;

/// <summary>
/// Every source of a release archive failed: unreachable, an error status, a
/// truncated body, or bytes that do not hash to what the release states. The
/// message names each source with its reason, and the inner exception is the
/// last transport error, when there was one.
/// </summary>
public sealed class DownloadFailedException : Exception
{
    public DownloadFailedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
