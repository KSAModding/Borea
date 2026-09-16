namespace Borea.Core.Instances;

/// <summary>
/// A file in a save or vehicle folder is open in another program, usually the game.
/// </summary>
public sealed class GameSaveInUseException : IOException
{
    public GameSaveInUseException(string folder, Exception innerException)
        : base($"A file in {folder} is open in another program.", innerException)
    {
        Folder = folder;
    }

    public string Folder { get; }
}
