using Borea.Core.Launch;

namespace Borea.Core.ModLoaders;

/// <summary>
/// The profile of a loader that Borea knows only from its listing. Its output is read for .NET errors alone, so no mod
/// is blamed for the mod the loader was loading, and no mod.toml table or install folder belongs to it.
/// </summary>
public sealed class NeutralLoaderProfile : ILoaderProfile
{
    public static NeutralLoaderProfile Instance { get; } = new();

    private NeutralLoaderProfile()
    {
    }

    public string? LoaderId => null;

    public IReadOnlyList<string> CodeNamespaces => [];

    public string? ModTomlSection => null;

    public bool LoadsModWithoutTable => false;

    public string? UninstallDisplayNamePrefix => null;

    public IReadOnlyList<string> InstallFolderNames => [];

    public IReadOnlyList<string> ProcessNames => [];

    public bool FoundNoGame(IEnumerable<string> output) => false;

    public bool StoppedWhileLoadingMods(IEnumerable<string> output) => false;

    public bool StoppedWhileLoaderStarted(IEnumerable<string> output) => false;

    public string? LikelyLoadingMod(IReadOnlyList<string> output, IReadOnlyList<LoadOrderMod> enabledMods, bool isWholeOutput = false) => null;
}
