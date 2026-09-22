using System.Diagnostics;

namespace Borea.Storage.Updates;

/// <summary>
/// Starts the new build as a process of its own. Its streams are not redirected, because the
/// process that starts it ends right after and a pipe into an ended process would break.
/// </summary>
public sealed class HandoverStarter : IHandoverStarter
{
    public void Start(string programPath, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programPath);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = programPath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(programPath)),
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var started = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"No process was started for '{programPath}'.");
    }
}
