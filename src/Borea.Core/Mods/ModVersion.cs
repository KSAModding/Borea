namespace Borea.Core.Mods
{
    /// <summary>
    /// A semantic version (Major.Minor.Patch[-PreRelease]) used for mod versioning
    /// and dependency comparisons throughout Borea.Core.
    /// </summary>
    public readonly record struct ModVersion : IComparable<ModVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        /// <summary>
        /// Optional pre-release label (e.g. "beta.1"). A version with a pre-release
        /// label is considered lower precedence than the same version without one,
        /// per standard semver rules.
        /// </summary>
        public string? PreRelease { get; }

        public ModVersion(int major, int minor, int patch, string? preRelease = null)
        {
            if (major < 0 || minor < 0 || patch < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(major), "Version components cannot be negative.");
            }

            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease;
        }

        public static ModVersion Parse(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw new FormatException($"'{value}' is not a valid mod version.");
            }

            return result;
        }

        public static bool TryParse(string? value, out ModVersion result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var corePart = value;
            string? preRelease = null;

            var dashIndex = value.IndexOf('-');
            if (dashIndex >= 0)
            {
                corePart = value[..dashIndex];
                preRelease = value[(dashIndex + 1)..];

                if (string.IsNullOrWhiteSpace(preRelease))
                {
                    return false;
                }
            }

            var parts = corePart.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var major) ||
                !int.TryParse(parts[1], out var minor) ||
                !int.TryParse(parts[2], out var patch))
            {
                return false;
            }

            if (major < 0 || minor < 0 || patch < 0)
            {
                return false;
            }

            result = new ModVersion(major, minor, patch, preRelease);
            return true;
        }

        public int CompareTo(ModVersion other)
        {
            var majorCompare = Major.CompareTo(other.Major);
            if (majorCompare != 0) return majorCompare;

            var minorCompare = Minor.CompareTo(other.Minor);
            if (minorCompare != 0) return minorCompare;

            var patchCompare = Patch.CompareTo(other.Patch);
            if (patchCompare != 0) return patchCompare;

            // No pre-release outranks any pre-release (1.0.0 > 1.0.0-beta).
            if (PreRelease is null && other.PreRelease is null) return 0;
            if (PreRelease is null) return 1;
            if (other.PreRelease is null) return -1;

            return string.CompareOrdinal(PreRelease, other.PreRelease);
        }

        public static bool operator <(ModVersion left, ModVersion right) => left.CompareTo(right) < 0;
        public static bool operator >(ModVersion left, ModVersion right) => left.CompareTo(right) > 0;
        public static bool operator <=(ModVersion left, ModVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >=(ModVersion left, ModVersion right) => left.CompareTo(right) >= 0;

        public override string ToString() => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
    }
}
