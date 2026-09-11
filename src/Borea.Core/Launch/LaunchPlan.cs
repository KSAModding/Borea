using System.Collections.ObjectModel;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// What a launch runs: the executable, its arguments, the working directory,
/// and the environment variables added to Borea's own.
/// </summary>
public sealed class LaunchPlan
{
    public string Executable { get; }

    /// <summary>One entry per argument, never a joined command line.</summary>
    public IReadOnlyList<string> Arguments { get; }

    public string WorkingDirectory { get; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    public LaunchPlan(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        Executable = Absolute(executable, nameof(executable));
        WorkingDirectory = Absolute(workingDirectory, nameof(workingDirectory));

        if (arguments is null)
            throw new ArgumentNullException(nameof(arguments));

        var copied = arguments.ToArray();
        if (copied.Any(argument => argument is null))
            throw new ArgumentException("An argument cannot be null.", nameof(arguments));

        if (environmentVariables is null)
            throw new ArgumentNullException(nameof(environmentVariables));

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in environmentVariables)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An environment variable needs a name.", nameof(environmentVariables));

            if (value is null)
                throw new ArgumentException($"The environment variable '{name}' cannot be null.", nameof(environmentVariables));

            variables[name] = value;
        }

        Arguments = new ReadOnlyCollection<string>(copied);
        EnvironmentVariables = new ReadOnlyDictionary<string, string>(variables);
    }

    /// <summary>
    /// The launch target under the loader directory with the separator
    /// translated (RFC 0035 rule 2), started in that directory (rule 5), with
    /// the instance root handed over the way the loader takes it.
    /// </summary>
    public static LaunchPlan ForLoader(string loaderDirectory, string launch, InstanceHandover handover, string instanceRoot)
    {
        if (handover is null)
            throw new ArgumentNullException(nameof(handover));

        var directory = Absolute(loaderDirectory, nameof(loaderDirectory));
        var root = Absolute(instanceRoot, nameof(instanceRoot));
        var target = RelativePaths.Contained(launch, nameof(launch))
            ?? throw new ArgumentException("The launch target is required.", nameof(launch));

        var executable = Path.Combine(directory, target.Replace('/', Path.DirectorySeparatorChar));
        var arguments = handover.Flag is null ? Array.Empty<string>() : new[] { handover.Flag, root };
        var variables = new Dictionary<string, string>();
        if (handover.Variable is not null)
            variables[handover.Variable] = root;

        return new LaunchPlan(executable, arguments, directory, variables);
    }

    private static string Absolute(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path is required.", paramName);

        // Fully qualified, because a rooted path can still lack its drive on Windows.
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The path must be absolute, because the loader resolves it in its own working directory.", paramName);

        return path;
    }
}
