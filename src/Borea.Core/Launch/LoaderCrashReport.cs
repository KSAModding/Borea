using System.Text.RegularExpressions;

namespace Borea.Core.Launch;

/// <summary>
/// Reads the output a loader left when it stopped, for the assemblies its
/// .NET exceptions name. A mod whose assembly is named is the likely cause.
/// </summary>
public static partial class LoaderCrashReport
{
    /// <summary>
    /// The assembly names in the output, in the order they first appear, from
    /// messages such as "from assembly 'KSArmory, Version=0.8.44.0'" and
    /// "Could not load file or assembly 'KSArmory, Version=…'".
    /// </summary>
    public static IReadOnlyList<string> AssemblyNames(IEnumerable<string> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var names = new List<string>();
        foreach (var line in output)
        {
            foreach (Match match in AssemblyPattern().Matches(line ?? string.Empty))
            {
                var name = match.Groups["name"].Value.Trim();
                if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }
        }

        return names;
    }

    [GeneratedRegex(@"assembly '(?<name>[^',]+)(,|')", RegexOptions.IgnoreCase)]
    private static partial Regex AssemblyPattern();
}
