using Borea.Core.Mods;

namespace Borea.Core.Instances;

public sealed record ModListEntry
{
    public string ModId { get; }

    public ModVersion Version { get; }

    public bool Enabled { get; }

    public ModListEntry(string modId, ModVersion version, bool enabled)
    {
        ModIds.Validate(modId, nameof(modId));

        ModId = modId;
        Version = version;
        Enabled = enabled;
    }
}
