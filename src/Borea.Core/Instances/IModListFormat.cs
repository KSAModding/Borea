namespace Borea.Core.Instances;

public interface IModListFormat
{
    string Write(ModList modList);

    /// <exception cref="UnsupportedModListFormatException">The file has a format version newer than <see cref="ModList.CurrentFormat"/>.</exception>
    /// <exception cref="FormatException">The text is not a modlist.</exception>
    ModList Read(string text);
}
