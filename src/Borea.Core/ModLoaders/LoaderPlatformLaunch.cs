using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// One entry of the [provides.platform] table of RFC 0067.
/// </summary>
public sealed class LoaderPlatformLaunch
{
    /// <summary>Relative to the loader's install location.</summary>
    public string Launch { get; }

    /// <summary>Null means <see cref="Launch"/> is the executable.</summary>
    public LoaderRuntime? Runtime { get; }

    /// <summary>The runtime as the listing names it.</summary>
    public string? RuntimeName { get; }

    /// <summary>The keys of the entry that Borea does not know.</summary>
    public IReadOnlyList<string> UnknownKeys { get; }

    public LoaderPlatformLaunch(string launch, string? runtime = null, IReadOnlyList<string>? unknownKeys = null)
    {
        Launch = RelativePaths.Contained(launch, nameof(launch))
            ?? throw new ArgumentException("The launch file is required.", nameof(launch));
        RuntimeName = runtime;
        Runtime = runtime switch
        {
            null => null,
            "dotnet" => LoaderRuntime.Dotnet,
            _ => LoaderRuntime.Unknown,
        };
        UnknownKeys = unknownKeys?.ToArray() ?? [];
    }
}
