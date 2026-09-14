namespace Borea.Core.Logging;

/// <summary>
/// The executable that wrote a line, so the App and the CLI can share one file.
/// </summary>
public enum BoreaLogSource
{
    App,
    Cli,
}
