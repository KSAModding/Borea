using System.Text.Json;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// The shared .NET runtime that a framework-dependent program needs, as its
/// runtimeconfig.json names it, and which installed versions the .NET host
/// accepts for it.
/// </summary>
public sealed class DotnetRuntimeNeed
{
    /// <summary>The framework of the .NET runtime itself, which every other shared framework builds on.</summary>
    public const string FrameworkName = "Microsoft.NETCore.App";

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly RollForward _rollForward;

    private DotnetRuntimeNeed(ModVersion version, RollForward rollForward)
    {
        Version = version;
        _rollForward = rollForward;
    }

    /// <summary>The lowest version the program asks for.</summary>
    public ModVersion Version { get; }

    /// <summary>The .NET release that <see cref="Version"/> belongs to, such as 10.0, as Microsoft names it.</summary>
    public string Channel => $"{Version.Major}.0";

    /// <summary>Microsoft's download page for the runtimes of <see cref="Channel"/>.</summary>
    public Uri DownloadPage => new($"https://dotnet.microsoft.com/download/dotnet/{Channel}");

    /// <summary>
    /// The need that the text of a runtimeconfig.json states, or null when it
    /// names no <see cref="FrameworkName"/> framework, which a self-contained
    /// program does not, or when Borea cannot read it.
    /// </summary>
    public static DotnetRuntimeNeed? Parse(string runtimeConfig)
    {
        ArgumentNullException.ThrowIfNull(runtimeConfig);
        try
        {
            using var document = JsonDocument.Parse(runtimeConfig, Options);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("runtimeOptions", out var options)
                || options.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var frameworks = new List<JsonElement>();
            if (options.TryGetProperty("framework", out var single) && single.ValueKind == JsonValueKind.Object)
                frameworks.Add(single);

            if (options.TryGetProperty("frameworks", out var several) && several.ValueKind == JsonValueKind.Array)
                frameworks.AddRange(several.EnumerateArray().Where(framework => framework.ValueKind == JsonValueKind.Object));

            var runtime = frameworks.FirstOrDefault(framework => string.Equals(Text(framework, "name"), FrameworkName, StringComparison.OrdinalIgnoreCase));
            if (runtime.ValueKind != JsonValueKind.Object || !ModVersion.TryParse(Text(runtime, "version"), out var version))
                return null;

            // a setting on the framework reference wins over the one for the whole program, as in the host
            var rollForward = ParseRollForward(Text(runtime, "rollForward") ?? Text(options, "rollForward"));
            return new DotnetRuntimeNeed(version, rollForward);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the host would start the program with one of the installed
    /// versions, by the roll forward rule of its runtimeconfig.json. A
    /// pre-release counts only when the program asks for a pre-release, as
    /// the host does by default.
    /// </summary>
    public bool IsMetBy(IEnumerable<string> installedVersions)
    {
        ArgumentNullException.ThrowIfNull(installedVersions);
        foreach (var text in installedVersions)
        {
            if (!ModVersion.TryParse(text, out var installed) || installed < Version)
                continue;

            if (installed.PreRelease is not null && Version.PreRelease is null)
                continue;

            var accepted = _rollForward switch
            {
                RollForward.Disable => installed == Version,
                RollForward.LatestPatch => installed.Major == Version.Major && installed.Minor == Version.Minor,
                RollForward.Major => true,
                _ => installed.Major == Version.Major,
            };
            if (accepted)
                return true;
        }

        return false;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static RollForward ParseRollForward(string? value) => value?.ToUpperInvariant() switch
    {
        "LATESTPATCH" => RollForward.LatestPatch,
        "MAJOR" or "LATESTMAJOR" => RollForward.Major,
        "DISABLE" => RollForward.Disable,
        _ => RollForward.Minor,
    };

    /// <summary>
    /// Which versions the rollForward value of runtimeconfig.json lets the host
    /// take. The Latest values take the same versions as the others, and an
    /// unknown value reads as the default, Minor.
    /// </summary>
    private enum RollForward
    {
        Minor,
        LatestPatch,
        Major,
        Disable,
    }
}
