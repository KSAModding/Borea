using System.Text.Json.Nodes;
using Borea.Core.Mods;
using static Borea.Core.Stewardship.ReleaseJson;

namespace Borea.Core.Stewardship;

/// <summary>The stamped release files of a listing at one commit of the base branch, with the game release list of that commit.</summary>
/// <param name="Files">Every release file, newest first.</param>
/// <param name="GameVersions">The versions of game-versions.json, which resolve a month to a build.</param>
public sealed record ReleaseFiles(IReadOnlyList<ReleaseFile> Files, IReadOnlyList<string> GameVersions);

/// <param name="Path">The path of the file in content-index-releases.</param>
/// <param name="Text">The text of the file on the base branch.</param>
public sealed record ReleaseFile(string Version, string Path, string Text);

/// <summary>What a change does to release files, as <see cref="ReleaseAmendment.EffectOf"/> finds it.</summary>
public enum ReleaseChangeEffect
{
    /// <summary>Every file already says this.</summary>
    Unchanged,

    /// <summary>The change only narrows, which a steward alone may do.</summary>
    Narrows,

    /// <summary>The change widens a file, which needs the owner or the author's request.</summary>
    Widens,

    /// <summary>The change does not parse, or the checks refuse it even for the owner.</summary>
    Refused,
}

/// <summary>The values of a release file that an amendment can change, as the file states them.</summary>
public sealed record ReleaseFileValues(string? GameMin, string? GameMax, ReleaseFileLoader? Loader, IReadOnlyList<ReleaseFileDependency> Dependencies)
{
    /// <summary>The values of the text, or null when it is no JSON object.</summary>
    public static ReleaseFileValues? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        JsonNode? parsed;
        try
        {
            parsed = Parse(text);
        }
        catch (FormatException)
        {
            return null;
        }

        if (parsed is not JsonObject document)
            return null;

        var loader = document["loader"] is JsonObject entry && StringOf(entry["id"]) is { } loaderId
            ? new ReleaseFileLoader(loaderId, StringOf(entry["min"]), StringOf(entry["max"]))
            : null;
        var dependencies = document["dependencies"] is JsonArray list
            ? list.OfType<JsonObject>()
                .Where(dependency => StringOf(dependency["id"]) is not null)
                .Select(dependency => new ReleaseFileDependency(StringOf(dependency["id"])!, StringOf(dependency["kind"]) ?? string.Empty, StringOf(dependency["min"]), StringOf(dependency["max"]),
                    StringOf(dependency["source"])))
                .ToList()
            : [];
        return new ReleaseFileValues(StringOf(document["game_min"]), StringOf(document["game_max"]), loader, dependencies);
    }

    /// <summary>The entry of the dependency on <paramref name="id"/>, which matches without regard to case, or null.</summary>
    public ReleaseFileDependency? Dependency(string id) => Dependencies.FirstOrDefault(dependency => ModIds.Equals(dependency.Id, id));
}

public sealed record ReleaseFileLoader(string Id, string? Min, string? Max);

/// <param name="Kind">The kind as the file writes it, such as "optional".</param>
/// <param name="Source">"authored" when the listing declared the entry, "derived" when the stamper took it from the archive's mod.toml.</param>
public sealed record ReleaseFileDependency(string Id, string Kind, string? Min, string? Max, string? Source = null)
{
    /// <summary>Whether the listing declared the entry.</summary>
    public bool IsAuthored => Source == "authored";
}
