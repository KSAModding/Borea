namespace Borea.Core.Launch;

/// <summary>What the output of a loader that stopped early shows about the cause.</summary>
public enum LoaderCrashCause
{
    /// <summary>No cause Borea knows.</summary>
    Unknown = 0,

    /// <summary>The error names an assembly of the blamed mod.</summary>
    ModAssembly = 1,

    /// <summary>The loader stopped while it loaded mods. The blamed mod, if any, is the one it was likely loading.</summary>
    ModLoading = 2,
}
