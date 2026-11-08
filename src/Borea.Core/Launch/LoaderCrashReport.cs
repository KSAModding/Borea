using System.Buffers;
using System.Text.RegularExpressions;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// Reads the .NET errors in the output a loader left when it stopped, for the
/// mod whose assembly they name. What else the output of one loader says, such
/// as the mod it was loading, its <see cref="ILoaderProfile"/> reads.
/// </summary>
public static partial class LoaderCrashReport
{
    // what Windows does not allow in a file name, with the wildcards of a file search
    private static readonly SearchValues<char> NotInAFileName = SearchValues.Create(
        "\"*:<>?|" + string.Concat(Enumerable.Range(0, 32).Select(code => (char)code)));

    /// <summary>
    /// The assembly names in the output, in the order they first appear, from
    /// messages such as "from assembly 'KSArmory, Version=0.8.44.0'" and
    /// "Could not load file or assembly 'KSArmory, Version=...'". A name given
    /// as a path, which .NET uses for an assembly it loads from a file, counts
    /// by its file name without ".dll". A name that is not a plain file name
    /// after that is left out, because callers search mod folders for it.
    /// </summary>
    public static IReadOnlyList<string> AssemblyNames(IEnumerable<string> output) => Names(output, AssemblyPattern());

    /// <summary>
    /// The assemblies that .NET could not find or load, from messages such as
    /// "Could not load file or assembly 'Brutal.Core.Numerics, Version=...'",
    /// read the way <see cref="AssemblyNames"/> reads them. An assembly named in
    /// "from assembly 'X'" exists, so it is left out.
    /// </summary>
    public static IReadOnlyList<string> MissingAssemblyNames(IEnumerable<string> output) => Names(output, MissingAssemblyPattern());

    private static List<string> Names(IEnumerable<string> output, Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(output);

        var names = new List<string>();
        foreach (var line in output)
        {
            foreach (Match match in pattern.Matches(line ?? string.Empty))
            {
                var name = AssemblyFileName(match.Groups["name"].Value);
                if (name is not null && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }
        }

        return names;
    }

    // Unlike Path.GetFileName, both separators count on every system, so a
    // Windows path and a POSIX path give the same name wherever Borea runs.
    private static string? AssemblyFileName(string name)
    {
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            name = name[..^".dll".Length].TrimEnd();

        return name.Trim('.').Length == 0 || name.AsSpan().ContainsAny(NotInAFileName) ? null : name;
    }

    /// <summary>
    /// Whether the output names an assembly of the game, which is KSA or a Brutal
    /// assembly. Only when the loader also stopped while it started itself, see
    /// <see cref="ILoaderProfile.StoppedWhileLoaderStarted"/>, was it made for another game build.
    /// </summary>
    public static bool NamesGameAssembly(IEnumerable<string> output) => AssemblyNames(output).Any(IsGameAssembly);

    /// <summary>Whether the assembly name is one of the game, which is KSA or a name whose first part is KSA or Brutal.</summary>
    public static bool IsGameAssembly(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var root = name.Split('.')[0];
        return root.Equals("KSA", StringComparison.OrdinalIgnoreCase) || root.Equals("Brutal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The releases page of a loader on GitHub, or else its forum thread.</summary>
    public static string ReleasesPage(ModMetadata loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var github = loader.Releases?.Hosts.FirstOrDefault(host => string.Equals(host.Host, "github", StringComparison.OrdinalIgnoreCase));

        // ModMetadata requires a forums link on every type except a vehicle or a save, so a loader always has one.
        return github is null ? loader.Links["forums"] : $"https://github.com/{github.Reference}/releases";
    }

    [GeneratedRegex(@"assembly '(?<name>[^',]+)(,|')", RegexOptions.IgnoreCase)]
    private static partial Regex AssemblyPattern();

    [GeneratedRegex(@"Could not load file or assembly '(?<name>[^',]+)(,|')", RegexOptions.IgnoreCase)]
    private static partial Regex MissingAssemblyPattern();
}
