using System.Collections.ObjectModel;
using Borea.Core.Game;

namespace Borea.Core.Mods;

public sealed class ForeignMod
{
    private readonly IReadOnlyList<LocalModDependency> _dependencies;

    public string FolderName { get; }
    public string ModId => FolderName;
    public ModVersion? Version => null;
    public GameCompatibility Compatibility => GameCompatibility.Unknown;
    public IReadOnlyList<LocalModDependency> Dependencies => _dependencies;
    public string? DependencyReadError { get; }

    public ForeignMod(
        string folderName,
        IReadOnlyList<LocalModDependency>? dependencies = null,
        string? dependencyReadError = null)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("Folder name cannot be null or whitespace.", nameof(folderName));

        if (folderName is "." or ".."
            || folderName.IndexOf(Path.DirectorySeparatorChar) >= 0
            || folderName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            throw new ArgumentException("Folder name must not contain a path.", nameof(folderName));

        FolderName = folderName;
        _dependencies = new ReadOnlyCollection<LocalModDependency>((dependencies ?? Array.Empty<LocalModDependency>()).ToArray());
        DependencyReadError = dependencyReadError;
    }
}
