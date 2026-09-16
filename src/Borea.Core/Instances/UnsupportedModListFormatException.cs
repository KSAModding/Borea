namespace Borea.Core.Instances;

/// <summary>
/// A modlist file that a newer version of Borea wrote, in a format this version cannot read.
/// </summary>
public sealed class UnsupportedModListFormatException : FormatException
{
    public int Format { get; }

    public UnsupportedModListFormatException(int format)
        : base($"The modlist has format {format}, and this version of Borea reads only format {ModList.CurrentFormat}. Update Borea to read it.")
    {
        Format = format;
    }
}
