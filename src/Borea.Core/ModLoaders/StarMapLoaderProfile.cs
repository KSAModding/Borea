using System.Text.RegularExpressions;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// The profile of StarMap. StarMap reads the [StarMap] table of a mod.toml, and its GameSurveyer.TryLoadCoreAndGame
/// loads StarMap.Core and the game into its own process and then loads the mods. LoaderConfig.TryLoadConfig appends
/// KSA.dll to a game path that names a folder and takes any other value as the file, and StarMap exits with 0 when it
/// cannot start at all, for example without a game path. DocumentsPathPatches.TryGetOverride reads the instance flag
/// without case.
/// </summary>
public sealed partial class StarMapLoaderProfile : ILoaderProfile
{
    private const string InstancePathReport = "StarMap - Using Instance Path:";

    private const string GameNotFoundReport = "Could not find KSA.dll";

    /// <summary>The content id of StarMap.</summary>
    public const string Id = "StarMap";

    public string? LoaderId => Id;

    public IReadOnlyList<string> CodeNamespaces { get; } = ["StarMap"];

    public string? ModTomlSection => "StarMap";

    public bool LoadsModWithoutTable => true;

    public string? UninstallDisplayNamePrefix => "StarMap version";

    public IReadOnlyList<string> InstallFolderNames { get; } = ["StarMap"];

    /// <summary>GameSurveyer.TryLoadCoreAndGame loads the game assembly into the StarMap process.</summary>
    public IReadOnlyList<string> ProcessNames { get; } = ["StarMap"];

    /// <summary>
    /// Whether the loader found no KSA.dll at its game path. StarMap writes "StarMap - Could not find KSA.dll" and then
    /// the path it tried, and stops without an error code (LoaderConfig.TryLoadConfig).
    /// </summary>
    public bool FoundNoGame(IEnumerable<string> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Any(line => line?.Contains(GameNotFoundReport, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Whether a stack frame in the output lies in StarMap's mod loading, which
    /// is PrepareMods of ModLoader in StarMap 0.4 or of ModsLoader in StarMap 0.5,
    /// RuntimeMod.TryCreateMod or RuntimeMod.InitializeMod, after the game prepared
    /// the manifest.
    /// </summary>
    public bool StoppedWhileLoadingMods(IEnumerable<string> output)
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
    public bool StoppedWhileLoaderStarted(IEnumerable<string> output)
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
    /// Whether StarMap failed in its own clean up after the game had shut down. Program.Main disposes the GameSurveyer
    /// when GameSurveyer.RunGame returns, so an error with a GameSurveyer.Dispose frame and without a frame of RunGame
    /// or TryLoadCoreAndGame came after the game's session ended, for example when StarMapPatches.Dispose cannot
    /// remove a patch.
    /// </summary>
    public bool FailedAfterTheGameShutDown(IReadOnlyList<string> crashLog)
    {
        ArgumentNullException.ThrowIfNull(crashLog);

        var disposing = false;
        foreach (var line in crashLog)
        {
            if (GameSessionFramePattern().IsMatch(line ?? string.Empty))
                return false;
            disposing |= DisposeFramePattern().IsMatch(line ?? string.Empty);
        }

        return disposing;
    }

    /// <summary>
    /// The enabled mod StarMap was likely loading when it stopped, or null.
    /// PrepareMods loads the mods in manifest order and reports each one it
    /// loaded, skipped or delayed, so the first code mod it did not report is the
    /// one it was loading.
    /// </summary>
    /// <param name="isWholeOutput">Whether the output holds every line the loader wrote, because none was cut from its start.</param>
    public string? LikelyLoadingMod(IReadOnlyList<string> output, IReadOnlyList<LoadOrderMod> enabledMods, bool isWholeOutput = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(enabledMods);

        if (!StoppedWhileLoadingMods(output))
            return null;

        var (reported, settled, loadedFromManifest, delayed) = ReadModReports(output);

        // The reports of the first mods may have left the output buffer. StarMap 0.4
        // prints the instance path right before it loads the mods, which shows that
        // they are all there. StarMap 0.5 prints nothing before them, so only the
        // whole output shows that, and a report in it shows that Borea reads their format.
        if (!output.Any(line => line?.TrimStart().StartsWith(InstancePathReport, StringComparison.Ordinal) == true) && !(isWholeOutput && reported.Count > 0))
            return null;

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

        // TryLoadWaitingMods loads a waiting mod only when every dependency
        // still missing is optional
        return waiting.FirstOrDefault(mod => mod.Missing.All(id => mod.Mod!.OptionalDependencies.Contains(id, StringComparer.Ordinal))).Mod?.ModId;
    }

    private static ModReports ReadModReports(IReadOnlyList<string> output)
    {
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

        return new ModReports(reported, settled, loadedFromManifest, delayed);
    }

    private static LoadOrderMod? ManifestMod(IReadOnlyList<LoadOrderMod> enabledMods, string modId) =>
        enabledMods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));

    private sealed record ModReports(HashSet<string> Reported, HashSet<string> Settled, HashSet<string> LoadedFromManifest, List<(string ModId, string[] Missing)> Delayed);

    [GeneratedRegex(@"^\s*at\s+StarMap\.Core\.ModRepository\.(?:RuntimeMod\.(?:InitializeMod|TryCreateMod)|Mods?Loader\.PrepareMods)\b")]
    private static partial Regex ModLoadingFramePattern();

    [GeneratedRegex(@"^\s*at\s+StarMap\.GameSurveyer\.TryLoadCoreAndGame\b")]
    private static partial Regex LoaderStartFramePattern();

    [GeneratedRegex(@"^\s*at\s+StarMap\.GameSurveyer\.Dispose\b")]
    private static partial Regex DisposeFramePattern();

    [GeneratedRegex(@"^\s*at\s+StarMap\.GameSurveyer\.(?:RunGame|TryLoadCoreAndGame)\b")]
    private static partial Regex GameSessionFramePattern();

    [GeneratedRegex(@"^\s*at\s+StarMap\.Core\.ModRepository\.RuntimeMod\.TryCreateMod\b")]
    private static partial Regex TryCreateModFramePattern();

    [GeneratedRegex(@"^\s*at\s+KSA\.ModLibrary\.PrepareManifest\b")]
    private static partial Regex PrepareManifestFramePattern();

    // StarMap 0.4 writes a report as it is, and StarMap 0.5 quotes it after its
    // level, as in "StarMap info: 'StarMap - Loaded mod: X from manifest'"
    [GeneratedRegex(@"^\s*(?:StarMap (?<level>info|warning|error): ')?StarMap - (?<report>Loaded mod|Not loading mod|Delaying load of mod|Failed to initialize mod|Failed to load mod):\s*(?<id>.+?)(?<manifest> from manifest)?(?: due to missing dependencies: (?<dependencies>.*)| because .*| after .*)?(?(level)')\s*$")]
    private static partial Regex ModReportPattern();
}
