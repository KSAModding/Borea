using System.Globalization;

namespace Borea.Core.Mods
{
    /// <summary>
    /// A SemVer 2.0.0 version (Major.Minor.Patch[-PreRelease]) used for mod
    /// versioning and dependency comparisons throughout Borea.Core.
    /// Build metadata is accepted on parse and discarded: it never takes part
    /// in precedence, and Borea stores versions in their canonical form.
    /// Core components are int-sized; pre-release identifiers compare without
    /// a size limit.
    /// </summary>
    public readonly record struct ModVersion : IComparable<ModVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        /// <summary>
        /// Optional pre-release label (e.g. "beta.1"). A version with a
        /// pre-release label ranks below the same version without one.
        /// </summary>
        public string? PreRelease { get; }

        public ModVersion(int major, int minor, int patch, string? preRelease = null)
        {
            if (major < 0)
                throw new ArgumentOutOfRangeException(nameof(major), "Version components cannot be negative.");

            if (minor < 0)
                throw new ArgumentOutOfRangeException(nameof(minor), "Version components cannot be negative.");

            if (patch < 0)
                throw new ArgumentOutOfRangeException(nameof(patch), "Version components cannot be negative.");

            if (!string.IsNullOrEmpty(preRelease) && !IsValidPreRelease(preRelease))
            {
                throw new ArgumentException(
                    $"'{preRelease}' is not a valid SemVer pre-release label.", nameof(preRelease));
            }

            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = string.IsNullOrEmpty(preRelease) ? null : preRelease;
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

            // Build metadata comes after the pre-release, so it is cut first;
            // a '-' after the '+' belongs to the build, not the pre-release.
            var plusIndex = value.IndexOf('+');
            if (plusIndex >= 0)
            {
                if (!AreValidIdentifiers(value[(plusIndex + 1)..], allowLeadingZeros: true))
                {
                    return false;
                }

                value = value[..plusIndex];
            }

            var corePart = value;
            string? preRelease = null;

            var dashIndex = value.IndexOf('-');
            if (dashIndex >= 0)
            {
                corePart = value[..dashIndex];
                preRelease = value[(dashIndex + 1)..];

                if (!IsValidPreRelease(preRelease))
                {
                    return false;
                }
            }

            var parts = corePart.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!TryParseComponent(parts[0], out var major) ||
                !TryParseComponent(parts[1], out var minor) ||
                !TryParseComponent(parts[2], out var patch))
            {
                return false;
            }

            result = new ModVersion(major, minor, patch, preRelease);
            return true;
        }

        /// <summary>
        /// A core component is digits only, without a leading zero, sign, or
        /// whitespace (SemVer 2.0.0 item 2), and must fit an int.
        /// </summary>
        private static bool TryParseComponent(string part, out int value)
        {
            value = 0;

            if (!IsNumericIdentifier(part))
            {
                return false;
            }

            if (part.Length > 1 && part[0] == '0')
            {
                return false;
            }

            return int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
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

            return ComparePreRelease(PreRelease, other.PreRelease);
        }

        /// <summary>
        /// SemVer 2.0.0 pre-release precedence: identifier by identifier,
        /// numeric identifiers compare numerically and rank below
        /// alphanumeric ones, and with an equal prefix the shorter list ranks
        /// lower ("beta" before "beta.2", "beta.9" before "beta.10").
        /// </summary>
        private static int ComparePreRelease(string left, string right)
        {
            var leftIds = left.Split('.');
            var rightIds = right.Split('.');
            var shared = Math.Min(leftIds.Length, rightIds.Length);

            for (var i = 0; i < shared; i++)
            {
                var l = leftIds[i];
                var r = rightIds[i];
                var leftNumeric = IsNumericIdentifier(l);
                var rightNumeric = IsNumericIdentifier(r);

                int compare;
                if (leftNumeric && rightNumeric)
                {
                    // No leading zeros, so more digits means a larger number.
                    compare = l.Length != r.Length ? l.Length.CompareTo(r.Length) : string.CompareOrdinal(l, r);
                }
                else if (leftNumeric != rightNumeric)
                {
                    compare = leftNumeric ? -1 : 1;
                }
                else
                {
                    compare = string.CompareOrdinal(l, r);
                }

                if (compare != 0) return compare;
            }

            return leftIds.Length.CompareTo(rightIds.Length);
        }

        private static bool IsValidPreRelease(string label) =>
            AreValidIdentifiers(label, allowLeadingZeros: false);

        /// <summary>
        /// Dot-separated identifiers of ASCII letters, digits, and hyphens,
        /// none empty. Numeric pre-release identifiers must not have leading
        /// zeros, so their numeric comparison stays unambiguous.
        /// </summary>
        private static bool AreValidIdentifiers(string label, bool allowLeadingZeros)
        {
            if (label.Length == 0)
            {
                return false;
            }

            foreach (var identifier in label.Split('.'))
            {
                if (identifier.Length == 0)
                {
                    return false;
                }

                foreach (var c in identifier)
                {
                    if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                    {
                        return false;
                    }
                }

                if (!allowLeadingZeros && identifier.Length > 1 && identifier[0] == '0' && IsNumericIdentifier(identifier))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNumericIdentifier(string identifier)
        {
            foreach (var c in identifier)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }

            return identifier.Length > 0;
        }

        public static bool operator <(ModVersion left, ModVersion right) => left.CompareTo(right) < 0;
        public static bool operator >(ModVersion left, ModVersion right) => left.CompareTo(right) > 0;
        public static bool operator <=(ModVersion left, ModVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >=(ModVersion left, ModVersion right) => left.CompareTo(right) >= 0;

        public override string ToString() => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
    }
}
