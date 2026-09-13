using Borea.Core.Mods;

namespace Borea.Cli.Output;

/// <summary>
/// Writes one line each time an install enters a phase, for example
/// "Downloading library 1.0.0 (1 of 2)". Byte reports within a phase write
/// nothing, so a redirected stream stays readable. The lines go to the error
/// stream, next to warnings, and keep the output stream for the result.
/// </summary>
internal sealed class InstallProgressOutput : IProgress<InstallProgress>
{
    private readonly TextWriter _writer;
    private (int Step, string ModId, InstallPhase Phase)? _last;

    public InstallProgressOutput(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Report(InstallProgress value)
    {
        var current = (value.Step, value.ModId, value.Phase);
        if (_last == current)
            return;

        _last = current;
        var place = value.StepCount > 1 ? $" ({value.Step} of {value.StepCount})" : string.Empty;
        _writer.WriteLine($"{Verb(value.Phase)} {value.ModId} {value.Version}{place}");
    }

    private static string Verb(InstallPhase phase) => phase switch
    {
        InstallPhase.Downloading => "Downloading",
        InstallPhase.Extracting => "Extracting",
        InstallPhase.Configuring => "Configuring",
        _ => "Finishing",
    };
}
