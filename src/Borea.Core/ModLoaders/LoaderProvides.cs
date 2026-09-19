using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// The authored [provides] table of RFC 0035.
/// </summary>
public sealed class LoaderProvides
{
    public string? Launch { get; }

    /// <summary>
    /// Null means the loader reads none,
    /// Unknown means a manager must not guess which.
    /// </summary>
    public InstallAnchor? ContentDir { get; }

    /// <summary>Null means the anchor itself.</summary>
    public string? ContentPath { get; }

    /// <summary>The configuration file a manager may write.</summary>
    public LoaderConfigure? Configure { get; }

    /// <summary>
    /// How the loader is told which instance to run. Null means the loader
    /// does not run instances, and a manager must not guess a flag or a variable.
    /// </summary>
    public InstanceHandover? Instance { get; }

    /// <summary>What starts on a platform instead of <see cref="Launch"/>. Empty when every platform starts it.</summary>
    public IReadOnlyDictionary<OsPlatform, LoaderPlatformLaunch> Platforms { get; }

    public LoaderProvides(
        string? launch = null,
        InstallAnchor? contentDir = null,
        string? contentPath = null,
        LoaderConfigure? configure = null,
        InstanceHandover? instance = null,
        IReadOnlyDictionary<OsPlatform, LoaderPlatformLaunch>? platforms = null)
    {
        if (contentPath is not null && contentDir is null)
            throw new ArgumentException("A content path needs the content directory it sits below.", nameof(contentPath));

        Launch = RelativePaths.Contained(launch, nameof(launch));
        ContentDir = contentDir;
        ContentPath = RelativePaths.Contained(contentPath, nameof(contentPath));
        Configure = configure;
        Instance = instance;
        Platforms = platforms is null ? new Dictionary<OsPlatform, LoaderPlatformLaunch>() : new Dictionary<OsPlatform, LoaderPlatformLaunch>(platforms);
    }
}
