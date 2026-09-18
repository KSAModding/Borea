using System.CommandLine;

namespace Borea.Cli.Commands;

/// <summary>
/// The arguments after the first "--" on the command line, for the commands
/// that pass them on to the game. The parser never sees them, because it would
/// give the first one to an optional argument such as a loader id.
/// </summary>
internal sealed class PassThroughArguments
{
    private readonly HashSet<Command> _commands = [];

    /// <summary>The arguments after "--", or none when the command line has no "--".</summary>
    public IReadOnlyList<string> Values { get; private set; } = [];

    public void Accept(Command command) => _commands.Add(command);

    /// <summary>
    /// Parses the part before "--" when it names a command that takes
    /// arguments after "--", and the whole command line otherwise.
    /// </summary>
    public ParseResult Parse(Command root, IReadOnlyList<string> args)
    {
        var separator = args.ToList().IndexOf("--");
        if (separator >= 0)
        {
            var head = root.Parse(args.Take(separator).ToArray());
            if (_commands.Contains(head.CommandResult.Command))
            {
                Values = args.Skip(separator + 1).ToArray();
                return head;
            }
        }

        return root.Parse(args);
    }
}
