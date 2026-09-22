namespace Borea.Core.Game;

/// <summary>
/// One thing Borea takes for granted about Kitten Space Agency. RocketWerkz
/// promised none of them, so each one is checked against the installation.
/// </summary>
public enum GameAssumption
{
    /// <summary>KSA.dll in the game directory carries the build in its file version.</summary>
    GameAssembly,

    /// <summary>Content/manifest.toml of the game lists [[mods]] entries with an id.</summary>
    ContentManifest,

    /// <summary>Content/Versions holds the patch notes as JSON files.</summary>
    PatchNotes,

    /// <summary>A profile holds the mods, saves, Vehicles and logs folders of the game.</summary>
    ProfileLayout,

    /// <summary>The manifest.toml of a profile lists [[mods]] entries with an id and an enabled flag.</summary>
    ProfileManifest,

    /// <summary>A mod is a folder under mods, named by its id, with a mod.toml in it.</summary>
    ModFolder,

    /// <summary>The game writes its session logs under the logs folder of the profile.</summary>
    SessionLog,
}
