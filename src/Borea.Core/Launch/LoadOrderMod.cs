namespace Borea.Core.Launch;

/// <summary>
/// An enabled mod of the instance manifest, and whether its folder holds the
/// entry assembly the loader would load (RuntimeMod.TryCreateMod).
/// </summary>
public sealed record LoadOrderMod(string ModId, bool IsCodeMod)
{
    /// <summary>Whether its mod.toml exists but does not parse, which stops RuntimeMod.TryCreateMod.</summary>
    public bool HasInvalidDefinition { get; init; }

    /// <summary>The mod ids its mod.toml marks as optional dependencies.</summary>
    public IReadOnlyList<string> OptionalDependencies { get; init; } = [];
}
