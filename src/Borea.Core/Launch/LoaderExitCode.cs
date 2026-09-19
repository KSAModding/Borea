using System.Globalization;

namespace Borea.Core.Launch;

/// <summary>Shows the exit code of a loader that stopped early in a form a player can look up.</summary>
public static class LoaderExitCode
{
    /// <summary>
    /// The exit code, and on Windows also its hex value and the name of a
    /// common code, for example "-1073741819 (0xC0000005, access violation)".
    /// </summary>
    public static string Describe(int exitCode, bool windows)
    {
        var number = exitCode.ToString(CultureInfo.InvariantCulture);
        if (!windows)
            return number;

        var code = unchecked((uint)exitCode);
        var hex = "0x" + code.ToString("X8", CultureInfo.InvariantCulture);
        return Name(code) is { } name ? $"{number} ({hex}, {name})" : $"{number} ({hex})";
    }

    private static string? Name(uint code) => code switch
    {
        0xC0000005 => "access violation",
        0xC00000FD => "stack overflow",
        0xE0434352 => ".NET exception",
        0x80131506 => "CLR internal error",
        _ => null,
    };
}
