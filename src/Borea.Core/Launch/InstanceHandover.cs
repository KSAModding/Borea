namespace Borea.Core.Launch;

/// <summary>
/// The [provides.instance] table of RFC 0049. It says how a loader is told
/// which instance to run: a flag that takes the instance root as the next
/// argument, a variable that carries it, or both.
/// </summary>
public sealed class InstanceHandover
{
    /// <summary>The argument that precedes the instance root. Null when the loader takes no flag.</summary>
    public string? Flag { get; }

    /// <summary>The environment variable that carries the instance root. Null when the loader reads none.</summary>
    public string? Variable { get; }

    public InstanceHandover(string? flag, string? variable)
    {
        if (flag is null && variable is null)
            throw new ArgumentException("A handover needs a flag or a variable.", nameof(flag));

        Flag = Token(flag, "flag", nameof(flag));
        Variable = Token(variable, "variable", nameof(variable));
    }

    // Each key is one token, because a manager passes the flag as one argument
    // and sets the variable by its exact name.
    private static string? Token(string? value, string key, string paramName)
    {
        if (value is not null && (value.Length == 0 || value.Any(char.IsWhiteSpace)))
            throw new ArgumentException($"The {key}, if provided, must be one token without whitespace.", paramName);

        return value;
    }
}
