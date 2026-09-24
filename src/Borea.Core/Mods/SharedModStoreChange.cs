namespace Borea.Core.Mods;

/// <summary>What became of turning the shared mod store on or off.</summary>
public enum SharedModStoreChange
{
    Saved,

    /// <summary>Borea runs the game for an instance, or a KSA or StarMap process runs.</summary>
    GameRunning,

    /// <summary>Another Borea App or command runs and would keep linking to the store.</summary>
    BoreaRunning,
}
