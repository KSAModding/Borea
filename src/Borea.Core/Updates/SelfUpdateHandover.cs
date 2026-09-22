using System.Globalization;
using System.Security.Cryptography;

namespace Borea.Core.Updates;

/// <summary>
/// What a new build is told about the build it replaces. Borea starts the new build with this option,
/// the program file the old build was moved to, the process id of the old build, the token of the
/// receipt that the staging build left, and the way the old build ran. The new build removes that
/// file once the old process has ended.
/// </summary>
/// <param name="PreviousProgramPath">The program file of the old build, which the new build moved aside.</param>
/// <param name="Token">Proves that the build which reads this handover is the build that staged it.</param>
/// <param name="FromCommandLine">Whether the old build ran a command, so that the new build opens no window.</param>
public sealed record SelfUpdateHandover(string PreviousProgramPath, int PreviousProcessId, string Token, bool FromCommandLine = false)
{
    /// <summary>The first argument of a handover. It is followed by the program file, the process id, the token and the way the old build ran.</summary>
    public const string Option = "--finish-self-update";

    /// <summary>The last argument of a handover that a command started.</summary>
    private const string CommandLineMode = "command-line";

    /// <summary>The last argument of a handover that a window started.</summary>
    private const string AppMode = "app";

    /// <summary>A token for one staged build, long enough that nobody can name it without reading the receipt.</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>The arguments that start a new build with this handover.</summary>
    public IReadOnlyList<string> ToArguments()
        => [Option, PreviousProgramPath, PreviousProcessId.ToString(CultureInfo.InvariantCulture), Token, FromCommandLine ? CommandLineMode : AppMode];

    /// <summary>
    /// Takes a handover off the front of <paramref name="arguments"/>, which then holds what the
    /// program would have received without it. Null when the arguments do not begin with one.
    /// </summary>
    public static SelfUpdateHandover? Take(ref string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments is not [Option, var programPath, var processId, var token, var mode, ..]
            || string.IsNullOrWhiteSpace(programPath)
            || string.IsNullOrWhiteSpace(token)
            || !(string.Equals(mode, AppMode, StringComparison.Ordinal) || string.Equals(mode, CommandLineMode, StringComparison.Ordinal))
            || !int.TryParse(processId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        arguments = arguments[5..];
        return new SelfUpdateHandover(programPath, id, token, string.Equals(mode, CommandLineMode, StringComparison.Ordinal));
    }
}
