namespace Borea.Core.Launch;

/// <summary>What the output of a loader that stopped early shows about the cause.</summary>
public enum LoaderCrashCause
{
    /// <summary>No cause Borea knows.</summary>
    Unknown = 0,

    /// <summary>The error names an assembly of the blamed mod, or a missing assembly that the blamed mod references.</summary>
    ModAssembly = 1,

    /// <summary>The loader stopped while it loaded mods. The blamed mod, if any, is the one it was likely loading.</summary>
    ModLoading = 2,

    /// <summary>The loader could not load an assembly of the game while it started itself, so it was made for another game build.</summary>
    GameAssembly = 3,
}
