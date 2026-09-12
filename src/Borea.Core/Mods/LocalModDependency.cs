namespace Borea.Core.Mods;

public sealed record LocalModDependency
{
    public string ModId { get; }
    public bool Optional { get; }

    public LocalModDependency(string modId, bool optional)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        ModId = modId;
        Optional = optional;
    }
}
