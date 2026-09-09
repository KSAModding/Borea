namespace Borea.Cli;

internal static class ExitCodes
{
    /// <summary>The command completed the operation.</summary>
    public const int Done = 0;

    /// <summary>The command ran and the operation failed. The reason is on stderr.</summary>
    public const int Failed = 1;

    /// <summary>The command line did not parse. The parser's message is on stderr.</summary>
    public const int Usage = 2;
}
