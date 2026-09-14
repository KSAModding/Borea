using System.CommandLine;
using System.CommandLine.Parsing;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

/// <summary>
/// Parse-time checks on argument values. A value that fails one is a usage
/// error, so the command never runs and the process exits with
/// <see cref="ExitCodes.Usage"/>.
/// </summary>
internal static class ArgumentRules
{
    /// <summary>A required string that must carry something besides whitespace.</summary>
    public static Argument<string> Text(string name, string description)
    {
        var argument = new Argument<string>(name) { Description = description };
        argument.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                result.AddError($"The {name} cannot be empty.");
        });
        return argument;
    }

    /// <summary>A content id, checked against the id rules in <see cref="ModIds"/>.</summary>
    public static Argument<string> ContentId(string name, string description)
    {
        var argument = new Argument<string>(name) { Description = description };
        argument.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (!ModIds.IsValid(value))
                result.AddError($"'{value}' is not a valid content id.");
        });
        return argument;
    }

    /// <summary>A content id that can be left out, checked like <see cref="ContentId"/> when given.</summary>
    public static Argument<string?> OptionalContentId(string name, string description)
    {
        var argument = new Argument<string?>(name) { Description = description, Arity = ArgumentArity.ZeroOrOne };
        argument.Validators.Add(result =>
        {
            if (result.Tokens.Count == 0)
                return;

            var value = result.GetValueOrDefault<string?>();
            if (!ModIds.IsValid(value))
                result.AddError($"'{value}' is not a valid content id.");
        });
        return argument;
    }

    /// <summary>
    /// The instance a command acts on. Absent means the active instance, present
    /// and blank is a usage error, like a blank instance argument.
    /// </summary>
    public static Option<string?> Instance()
    {
        var option = new Option<string?>("--instance")
        {
            Description = "The instance to act on, by name or id. The active instance when absent.",
        };
        option.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string?>()))
                result.AddError("The --instance value cannot be empty.");
        });
        return option;
    }

    public static Option<bool> Json() => new("--json") { Description = "Print the result as JSON, for scripts." };

    /// <summary>A release channel name: stable, testing or dev.</summary>
    public static Argument<string> Channel(string name, string description)
    {
        var argument = new Argument<string>(name) { Description = description };
        argument.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (!ReleaseChannels.TryParse(value, out _))
                result.AddError(ChannelError(value));
        });
        return argument;
    }

    /// <summary>The release channel for one command. Absent means the saved channel.</summary>
    public static Option<string?> Channel()
    {
        var option = new Option<string?>("--channel")
        {
            Description = "The release channel for this command: stable, testing or dev. The saved channel when absent.",
        };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (!ReleaseChannels.TryParse(value, out _))
                result.AddError(ChannelError(value));
        });
        return option;
    }

    /// <summary>The channel the option names, or <paramref name="saved"/> when it is absent.</summary>
    public static ReleaseChannel ChannelOrSaved(string? value, ReleaseChannel saved)
        => value is not null && ReleaseChannels.TryParse(value, out var channel) ? channel : saved;

    private static string ChannelError(string? value)
        => $"'{value}' is not a release channel. Use {string.Join(", ", ReleaseChannels.Names)}.";
}
