namespace Borea.Core.Dependencies
{
    /// <summary>
    /// The kind of dependency a mod can have on another mod.
    /// </summary>
    public enum ModDependencyKind
    {
        /// <summary>
        /// The mod is required for the mod to function.
        /// </summary>
        required = 0,

        /// <summary>
        /// The mod is optional.
        /// </summary>
        optional = 1,

        /// <summary>
        /// The mod is recommended for the mod to function, but not required. Selected by default in mod managers.
        /// </summary>
        recommends = 2,

        /// <summary>
        /// Listed as a suggestion for the mod to function, but not required. Not selected by default in mod managers.
        /// </summary>
        suggests = 3,

        /// <summary>
        /// The mod is incompatible with the mod. The mod manager should prevent both mods from being installed at the same time.
        /// </summary>
        conflict = 4,
    }
}
