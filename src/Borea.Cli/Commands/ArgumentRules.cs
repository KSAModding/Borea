using System.CommandLine;
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

    public static Argument<string> ContentId(string name, string description)
    {
        var argument = new Argument<string>(name) { Description = description };
        argument.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (!ModIds.IsValid(value))
                result.AddError(ContentIdError(value));
        });
        return argument;
    }

    public static Argument<string?> OptionalContentId(string name, string description)
    {
        var argument = new Argument<string?>(name) { Description = description, Arity = ArgumentArity.ZeroOrOne };
        argument.Validators.Add(result =>
        {
            if (result.Tokens.Count == 0)
                return;

            var value = result.GetValueOrDefault<string?>();
            if (!ModIds.IsValid(value))
                result.AddError(ContentIdError(value));
        });
        return argument;
    }

    /// <summary>An option that may be absent but never blank, the option counterpart of <see cref="Text"/>.</summary>
    public static Option<string?> TextOption(string name, string description)
    {
        var option = new Option<string?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string?>()))
                result.AddError($"The {name} value cannot be empty.");
        });
        return option;
    }

    /// <summary>
    /// Absent means the active instance, present and blank is a usage error, like a blank instance argument.
    /// </summary>
    public static Option<string?> Instance()
        => TextOption("--instance", "The instance to act on, by name or id. The active instance when absent.");

    public static Option<bool> Json() => new("--json") { Description = "Print the result as JSON, for scripts." };

    public static Option<string?> Version(string description)
    {
        var version = new Option<string?>("--version") { Description = description };
        version.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (string.IsNullOrWhiteSpace(value) || !ModVersion.TryParse(value, out _))
                result.AddError($"'{value}' is not a valid semantic version.");
        });
        return version;
    }

    public static Option<bool> WithRecommended() => new("--with-recommended") { Description = "Install recommended dependencies." };

    public static Option<string[]> Alternatives() => new("--alternative") { Description = "Select a required alternative as choice-key=mod-id." };

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

    public static string ContentIdError(string? value) => $"'{value}' is not a valid content id.";
}
