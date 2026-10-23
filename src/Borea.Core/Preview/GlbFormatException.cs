namespace Borea.Core.Preview;

/// <summary>
/// Thrown when a file is not a GLB file that Borea can read. A caller treats it as a failed asset and shows a
/// placeholder. It is not an <see cref="IOException"/>, because an IOException means that the source is gone.
/// </summary>
public sealed class GlbFormatException : FormatException
{
    public GlbFormatException(string message)
        : base(message)
    {
    }

    public GlbFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
