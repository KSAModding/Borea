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

    public LoaderProvides(
        string? launch = null,
        InstallAnchor? contentDir = null,
        string? contentPath = null,
        LoaderConfigure? configure = null)
    {
        if (contentPath is not null && contentDir is null)
            throw new ArgumentException("A content path needs the content directory it sits below.", nameof(contentPath));

        Launch = RelativePaths.Contained(launch, nameof(launch));
        ContentDir = contentDir;
        ContentPath = RelativePaths.Contained(contentPath, nameof(contentPath));
        Configure = configure;
    }
}
