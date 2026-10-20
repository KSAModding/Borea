using System.Buffers;
using System.Text.RegularExpressions;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// Reads the output a loader left when it stopped, for the mod that likely
/// caused it, which is a mod whose assembly its .NET exceptions name or else
/// the mod it was loading.
/// </summary>
public static partial class LoaderCrashReport
{
    private const string InstancePathReport = "StarMap - Using Instance Path:";

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
    /// <see cref="StoppedWhileLoaderStarted"/>, was it made for another game build.
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
        return github is null ? loader.ForumUrl : $"https://github.com/{github.Reference}/releases";
    }

    /// <summary>
    /// Whether a stack frame in the output lies in StarMap's mod loading, which
    /// is ModLoader.PrepareMods (ModsLoader in newer StarMap), RuntimeMod.TryCreateMod
    /// or RuntimeMod.InitializeMod, after the game prepared the manifest.
    /// </summary>
    public static bool StoppedWhileLoadingMods(IEnumerable<string> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var loadingMods = false;
        foreach (var line in output)
        {
            if (PrepareManifestFramePattern().IsMatch(line ?? string.Empty))
                return false;
            loadingMods |= ModLoadingFramePattern().IsMatch(line ?? string.Empty);
        }

        return loadingMods;
    }

    /// <summary>
    /// Whether StarMap stopped while it started itself, before it loaded a mod.
    /// GameSurveyer.TryLoadCoreAndGame loads StarMap.Core and the game and then
    /// loads the mods, so its frame without a frame of the mod loading shows that.
    /// Output without frames, which a long error can push out of the buffer,
    /// shows nothing.
    /// </summary>
    public static bool StoppedWhileLoaderStarted(IEnumerable<string> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var starting = false;
        foreach (var line in output)
        {
            if (ModLoadingFramePattern().IsMatch(line ?? string.Empty))
                return false;
            starting |= LoaderStartFramePattern().IsMatch(line ?? string.Empty);
        }

        return starting;
    }

    /// <summary>
    /// The enabled mod StarMap was likely loading when it stopped, or null.
    /// ModLoader.PrepareMods loads the mods in manifest order and reports each
    /// one it loaded, skipped or delayed, so the first code mod it did not report
    /// is the one it was loading.
    /// </summary>
    public static string? LikelyLoadingMod(IReadOnlyList<string> output, IReadOnlyList<LoadOrderMod> enabledMods)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(enabledMods);

        // StarMap prints the instance path right before it loads the mods, so
        // without it the reports of the first mods may have left the output buffer
        if (!StoppedWhileLoadingMods(output) || !output.Any(line => line?.TrimStart().StartsWith(InstancePathReport, StringComparison.Ordinal) == true))
            return null;

        var reported = new HashSet<string>(ModIds.Comparer);
        var settled = new HashSet<string>(ModIds.Comparer);

        // StarMap looks up dependencies by the exact id the dependent's mod.toml gives
        var loadedFromManifest = new HashSet<string>(StringComparer.Ordinal);
        var delayed = new List<(string ModId, string[] Missing)>();
        foreach (var line in output)
        {
            var match = ModReportPattern().Match(line ?? string.Empty);
            if (!match.Success)
                continue;

            var modId = match.Groups["id"].Value.Trim();
            reported.Add(modId);
            switch (match.Groups["report"].Value)
            {
                case "Loaded mod":
                    settled.Add(modId);
                    if (match.Groups["manifest"].Success)
                        loadedFromManifest.Add(modId);
                    break;
                case "Delaying load of mod":
                    delayed.Add((modId, match.Groups["dependencies"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
                    break;
                case "Failed to initialize mod" or "Failed to load mod":
                    settled.Add(modId);
                    break;
            }
        }

        var waiting = delayed
            .Where(mod => !settled.Contains(mod.ModId))
            .Select(mod => (Mod: ManifestMod(enabledMods, mod.ModId), Missing: mod.Missing.Where(id => !loadedFromManifest.Contains(id)).ToList()))
            .Where(mod => mod.Mod is not null)
            .ToList();

        // RuntimeMod.CheckForDependentMods loads a delayed mod right after the
        // last of its dependencies loaded from the manifest, before the next mod
        if (waiting.FirstOrDefault(mod => mod.Missing.Count == 0).Mod is { } dependent)
            return dependent.ModId;

        var inTryCreateMod = output.Any(line => TryCreateModFramePattern().IsMatch(line ?? string.Empty));
        var next = enabledMods.FirstOrDefault(mod => (mod.IsCodeMod || (inTryCreateMod && mod.HasInvalidDefinition)) && !reported.Contains(mod.ModId));
        if (next is not null)
            return next.ModId;

        // ModLoader.TryLoadWaitingMods loads a waiting mod only when every
        // dependency still missing is optional
        return waiting.FirstOrDefault(mod => mod.Missing.All(id => mod.Mod!.OptionalDependencies.Contains(id, StringComparer.Ordinal))).Mod?.ModId;
    }

    private static LoadOrderMod? ManifestMod(IReadOnlyList<LoadOrderMod> enabledMods, string modId) =>
        enabledMods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));

    [GeneratedRegex(@"^\s*at\s+StarMap\.Core\.ModRepository\.(?:RuntimeMod\.(?:InitializeMod|TryCreateMod)|Mods?Loader\.PrepareMods)\b")]
    private static partial Regex ModLoadingFramePattern();

    [GeneratedRegex(@"^\s*at\s+StarMap\.GameSurveyer\.TryLoadCoreAndGame\b")]
    private static partial Regex LoaderStartFramePattern();

    [GeneratedRegex(@"^\s*at\s+StarMap\.Core\.ModRepository\.RuntimeMod\.TryCreateMod\b")]
    private static partial Regex TryCreateModFramePattern();

    [GeneratedRegex(@"^\s*at\s+KSA\.ModLibrary\.PrepareManifest\b")]
    private static partial Regex PrepareManifestFramePattern();

    [GeneratedRegex(@"^\s*StarMap - (?<report>Loaded mod|Not loading mod|Delaying load of mod|Failed to initialize mod|Failed to load mod):\s*(?<id>.+?)(?<manifest> from manifest)?(?: due to missing dependencies: (?<dependencies>.*)| because .*| after .*)?\s*$")]
    private static partial Regex ModReportPattern();

    [GeneratedRegex(@"assembly '(?<name>[^',]+)(,|')", RegexOptions.IgnoreCase)]
    private static partial Regex AssemblyPattern();

    [GeneratedRegex(@"Could not load file or assembly '(?<name>[^',]+)(,|')", RegexOptions.IgnoreCase)]
    private static partial Regex MissingAssemblyPattern();
}
