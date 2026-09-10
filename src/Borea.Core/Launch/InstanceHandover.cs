using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// How a loader is told which instance to run: a flag that takes the instance
/// root as the next argument, a variable that carries it, or both.
/// </summary>
public sealed class InstanceHandover
{
    // StarMap reads the flag first and the variable when the flag is absent.
    // Its own restart passes only --restarted, so the variable is what keeps
    // the instance across it.
    private static readonly Dictionary<string, InstanceHandover> KnownHandovers = new(ModIds.Comparer)
    {
        ["StarMap"] = new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH"),
    };

    /// <summary>The argument that precedes the instance root. Null when the loader takes no flag.</summary>
    public string? Flag { get; }

    /// <summary>The environment variable that carries the instance root. Null when the loader reads none.</summary>
    public string? Variable { get; }

    public InstanceHandover(string? flag, string? variable)
    {
        if (flag is not null && string.IsNullOrWhiteSpace(flag))
            throw new ArgumentException("A flag, if provided, cannot be whitespace.", nameof(flag));

        if (variable is not null && string.IsNullOrWhiteSpace(variable))
            throw new ArgumentException("A variable, if provided, cannot be whitespace.", nameof(variable));

        if (flag is null && variable is null)
            throw new ArgumentException("A handover needs a flag or a variable.", nameof(flag));

        Flag = flag;
        Variable = variable;
    }

    /// <summary>The handover Borea knows for the loader, or null.</summary>
    public static InstanceHandover? Known(string loaderId)
    {
        ModIds.Validate(loaderId, nameof(loaderId));

        return KnownHandovers.TryGetValue(loaderId, out var handover) ? handover : null;
    }
}
