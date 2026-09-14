namespace Borea.Storage.Launch;

internal static class DotnetHost
{
    public static string? AssemblyBeside(string executable)
    {
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        var assembly = Path.ChangeExtension(executable, ".dll");
        return File.Exists(assembly) ? assembly : null;
    }

    /// <summary>The first dotnet file in the absolute directories of a PATH value, or null.</summary>
    public static string? Find(string? searchPath)
    {
        if (string.IsNullOrEmpty(searchPath))
            return null;

        foreach (var directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(directory))
                continue;

            var candidate = Path.Combine(directory, "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
