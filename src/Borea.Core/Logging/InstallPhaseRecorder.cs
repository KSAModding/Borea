using Borea.Core.Mods;

namespace Borea.Core.Logging;

/// <summary>
/// Passes install reports on and remembers the last phase, so a failure can
/// name the step it stopped in.
/// </summary>
internal sealed class InstallPhaseRecorder(IProgress<InstallProgress>? inner) : IProgress<InstallProgress>
{
    public InstallPhase? Phase { get; private set; }

    public string PhaseText => Phase is { } phase ? $"while {phase.ToString().ToLowerInvariant()}" : "before the download";

    public void Report(InstallProgress value)
    {
        Phase = value.Phase;
        inner?.Report(value);
    }
}
