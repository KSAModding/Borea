using Borea.Core.Mods;

namespace Borea.Core.Updates;

/// <summary>A checked new build, unpacked beside the running one and ready to take its place.</summary>
public sealed class StagedSelfUpdate
{
    private readonly Action _install;

    private readonly Action _handOver;

    public ModVersion Version { get; }

    /// <summary>The folder Borea runs in, which the new build is put into.</summary>
    public string Folder { get; }

    /// <summary>The program file, which keeps its path over the update.</summary>
    public string ProgramPath { get; }

    /// <summary>Where <see cref="ProgramPath"/> is moved to while the new build takes its place.</summary>
    public string ReplacedProgramPath { get; }

    public StagedSelfUpdate(ModVersion version, string folder, string programPath, string replacedProgramPath, Action install, Action handOver)
    {
        Version = version;
        Folder = folder ?? throw new ArgumentNullException(nameof(folder));
        ProgramPath = programPath ?? throw new ArgumentNullException(nameof(programPath));
        ReplacedProgramPath = replacedProgramPath ?? throw new ArgumentNullException(nameof(replacedProgramPath));
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _handOver = handOver ?? throw new ArgumentNullException(nameof(handOver));
    }

    /// <summary>
    /// Puts the new build in the folder Borea runs in. The running program file is moved aside and
    /// put back when a step after that fails, so Borea keeps a build that works.
    /// </summary>
    /// <exception cref="SelfUpdateFailedException">The new build was not put in place.</exception>
    public void Install() => _install();

    /// <summary>
    /// Starts the new build, which removes the program file it replaced once this process has ended.
    /// Call it last, after <see cref="Install"/>, because the new build waits for this process.
    /// </summary>
    /// <exception cref="SelfUpdateFailedException">The new build did not start, and the replaced one was put back.</exception>
    public void HandOver() => _handOver();
}
