using System.Reflection;

namespace Borea.Core.Updates;

/// <summary>What the running Borea build reports about itself.</summary>
public static class BoreaBuild
{
    /// <summary>
    /// The version the release workflow stamped, with the commit after the "+".
    /// A local build reports 1.0.0 plus the commit it was built from.
    /// </summary>
    public static string InformationalVersion { get; } = ReadVersion();

    /// <summary><see cref="InformationalVersion"/> without the build metadata.</summary>
    public static string Version { get; } = InformationalVersion.Split('+')[0];

    private static string ReadVersion()
    {
        var assembly = typeof(BoreaBuild).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational;

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
