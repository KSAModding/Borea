using System.Text;

namespace Borea.Core.Launch;

/// <summary>
/// Launch arguments written as one line of text. Both directions follow the
/// rules of the Windows function CommandLineToArgvW on every platform, so a
/// line splits the same way wherever Borea runs.
/// </summary>
public static class ArgumentLine
{
    /// <summary>
    /// The arguments in <paramref name="text"/>. Spaces and tabs outside double
    /// quotes separate them, and a backslash escapes the double quote after it.
    /// </summary>
    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var arguments = new List<string>();
        var current = new StringBuilder();
        var inArgument = false;
        var quotes = 0;
        var backslashes = 0;
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index++];
            if (character is ' ' or '\t' && quotes == 0)
            {
                if (inArgument)
                    arguments.Add(current.ToString());

                current.Clear();
                inArgument = false;
                backslashes = 0;
                continue;
            }

            inArgument = true;
            if (character != '"')
            {
                current.Append(character);
                backslashes = character == '\\' ? backslashes + 1 : 0;
                continue;
            }

            // 2n backslashes before a quote give n backslashes and a quote that
            // opens or closes a quoted part, 2n+1 give n backslashes and a literal quote
            current.Length -= backslashes / 2 + backslashes % 2;
            if (backslashes % 2 == 0)
                quotes++;
            else
                current.Append('"');

            backslashes = 0;

            // in a run of quotes, CommandLineToArgvW writes every third one as a literal quote
            while (index < text.Length && text[index] == '"')
            {
                index++;
                if (++quotes == 3)
                {
                    current.Append('"');
                    quotes = 0;
                }
            }

            if (quotes == 2)
                quotes = 0;
        }

        if (inArgument)
            arguments.Add(current.ToString());

        return arguments;
    }

    /// <summary>The arguments as one line that <see cref="Split"/> turns back into the same arguments.</summary>
    public static string Join(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var line = new StringBuilder();
        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument, nameof(arguments));
            if (line.Length > 0)
                line.Append(' ');

            AppendArgument(line, argument);
        }

        return line.ToString();
    }

    private static void AppendArgument(StringBuilder line, string argument)
    {
        if (argument.Length > 0 && !argument.Any(character => character == '"' || char.IsWhiteSpace(character)))
        {
            line.Append(argument);
            return;
        }

        line.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            // backslashes count double before a quote
            line.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            line.Append(character);
            backslashes = 0;
        }

        line.Append('\\', backslashes * 2);
        line.Append('"');
    }
}
