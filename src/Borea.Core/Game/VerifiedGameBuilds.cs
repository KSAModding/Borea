using System.Reflection;
using System.Text.Json;

namespace Borea.Core.Game;

/// <summary>
/// The game builds the assumptions in <see cref="GameAssumption"/> were checked
/// against, from verified-builds.json next to this file. The list is kept in
/// the repository, because the game's own version list says which builds exist
/// and not which ones anybody tested.
/// </summary>
public sealed class VerifiedGameBuilds
{
    private const string ResourceName = "Borea.Core.Game.verified-builds.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The list this build of Borea was compiled with.</summary>
    public static VerifiedGameBuilds Current { get; } = Load();

    /// <summary>Every verified build, oldest first.</summary>
    public IReadOnlyList<GameVersion> Builds { get; }

    /// <summary>The newest build anybody checked.</summary>
    public GameVersion Newest => Builds[^1];

    private VerifiedGameBuilds(IReadOnlyList<GameVersion> builds)
    {
        Builds = builds;
    }

    /// <summary>Whether the installed build is past everything that was checked.</summary>
    public bool IsPastNewest(GameVersion build) => build > Newest;

    private static VerifiedGameBuilds Load()
    {
        using var stream = typeof(VerifiedGameBuilds).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing.");

        var document = JsonSerializer.Deserialize<VerifiedBuildsDto>(stream, JsonOptions);
        var builds = document?.Builds?
            .Select(entry => GameVersion.TryParse(entry?.Build, out var build) ? build : (GameVersion?)null)
            .OfType<GameVersion>()
            .OrderBy(build => build)
            .ToList();

        return builds is { Count: > 0 }
            ? new VerifiedGameBuilds(builds)
            : throw new InvalidOperationException($"'{ResourceName}' names no game build that parses.");
    }

    private sealed class VerifiedBuildsDto
    {
        public List<VerifiedBuildDto?>? Builds { get; set; }
    }

    private sealed class VerifiedBuildDto
    {
        public string? Build { get; set; }
    }
}
