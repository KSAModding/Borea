using Borea.Core.ModLoaders;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using System.Security.Cryptography;

namespace Borea.Core.Mods
{
    /// <summary>
    /// The versioned metadata for a mod. Contains information about the specific version of the mod.
    /// </summary>
    public class ModVersionMetadata
    {
        /// <summary>
        /// The version of the metadata specification.
        /// </summary>
        public int SpecVersion { get; }

        /// <summary>
        /// The mod's unique identifier.
        /// </summary>
        public string ModId { get; }

        /// <summary>
        /// The version of the mod.
        /// </summary>
        public ModVersion Version { get; }

        /// <summary>
        /// The release status of the mod version ("stable", "testing", or "dev").
        /// </summary>
        public string ReleaseStatus { get; }

        /// <summary>
        /// The release date of the mod version.
        /// </summary>
        public DateTime ReleaseDate { get; }

        /// <summary>
        /// The minimum game version required for this mod version.
        /// </summary>
        public GameVersion MinGameVersion { get; }
        /// <summary>
        /// The maximum game version allowed for this mod version.
        /// </summary>
        public GameVersion? MaxGameVersion { get; }
        /// <summary>
        /// The operating systems compatible with this mod version.
        /// </summary>
        public string[] OScompatibility { get; }

        /// <summary>
        /// The SHA256 hash of the mod version file.
        /// </summary>
        public SHA256 Hash { get; }

        /// <summary>
        /// The size of the mod version file in bytes.
        /// </summary>
        public string Size { get; }

        /// <summary>
        /// The mod loader required for this mod version, if any.
        /// </summary>
        public string ModLoader { get; }

        /// <summary>
        /// The minimum version of the mod loader required for this mod version, if any.
        /// </summary>
        public string MinLoaderVersion { get; }

        /// <summary>
        /// The maximum version of the mod loader allowed for this mod version, if any.
        /// </summary>
        public string MaxLoaderVersion { get; }

        /// <summary>
        /// The dependencies required for this mod version.
        /// </summary>
        public IReadOnlyList<ModDependency> Dependencies { get; }

        /// <summary>
        /// The changelog for this mod version.
        /// </summary>
        public string ChangelogURL { get; }

        /// <param name="hash">SHA256 hash of the mod version file</param>
        /// <param name="size">Size of the mod version file in bytes</param>
        public ModVersionMetadata(
            int specVersion,
            string modId,
            ModVersion version,
            string releaseStatus,
            DateTime releaseDate,
            GameVersion minGameVersion,
            GameVersion? maxGameVersion,
            string[] osCompatibility,
            SHA256 hash,
            string size,
            string modLoader,
            string minLoaderVersion,
            IReadOnlyList<ModDependency> dependencies,
            string changelogURL,
            string maxLoaderVersion = "")
        {
            SpecVersion = specVersion;
            ModId = modId;
            Version = version;
            ReleaseStatus = releaseStatus;
            ReleaseDate = releaseDate;
            MinGameVersion = minGameVersion;
            MaxGameVersion = maxGameVersion;
            OScompatibility = osCompatibility;
            Hash = hash;
            Size = size;
            ModLoader = modLoader;
            MinLoaderVersion = minLoaderVersion;
            MaxLoaderVersion = maxLoaderVersion;
            Dependencies = dependencies;
            ChangelogURL = changelogURL;
        }
    }
}
