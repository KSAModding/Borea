using Borea.Core.Mods;

namespace Borea.Core.Instances;

/// <summary>
/// Describes the origin of an <see cref="Instance"/>: either a specific modpack at a
/// specific version, or a user-curated set of mods with no modpack backing.
/// </summary>
public abstract record InstanceSource
{
    private InstanceSource()
    {
    }

    /// <summary>
    /// An instance materialized from a specific modpack at a specific version.
    /// </summary>
    public sealed record FromModPack(string ModPackId, ModVersion Version) : InstanceSource;

    /// <summary>
    /// A user-curated instance with no modpack origin.
    /// </summary>
    public sealed record Custom : InstanceSource
    {
        public static readonly Custom Value = new();

        private Custom()
        {
        }
    }
}